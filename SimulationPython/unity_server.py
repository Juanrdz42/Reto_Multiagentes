"""Local pull API: Unity starts runs, polls status, downloads a complete JSON."""
import argparse
import json
import threading
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import m4_final_model as model

model.FRAME_SAMPLE = 1
original_record = model.DistributionCenter._record_frame
progress_context = threading.local()


def record(self):
    original_record(self)
    frame = self.history[-1]
    for snapshot, agv in zip(frame["agvs"], self.agvs):
        snapshot["charge_phase"] = agv.charge_phase
    locations = {s.current_pallet_id: s.station_id for s in self.stations if s.current_pallet_id}
    for i, outlet in enumerate(self.production_line.outlets):
        if outlet["pallet_id"]:
            locations[outlet["pallet_id"]] = f"Prod Line {i + 1}"
    for pallet in frame["pallets"]:
        pallet["storage_zone"] = locations.get(pallet["id"], "")
        pallet["storage_level"] = int(pallet["id"][1:]) % 2
        pallet["removed"] = pallet["state"] == model.P_DELIVERED and not pallet["storage_zone"]
    callback = getattr(progress_context, "callback", None)
    if callback:
        callback()


model.DistributionCenter._record_frame = record


def run_single(seed=42, steps=800):
    baseline = model.run_simulation(model.STRATEGY_BASELINE, steps=steps, seed=seed, routing=model.ROUTING_BFS)
    proposed = model.run_simulation(model.STRATEGY_PROPOSED, steps=steps, seed=seed, routing=model.ROUTING_ASTAR)
    base_metrics = model.compute_metrics(baseline)
    metrics = model.compute_metrics(proposed)
    return model._to_serializable({
        "meta": {"width": model.WIDTH, "height": model.HEIGHT, "cell_size": model.CELL_SIZE,
                 "frame_sample": 1, "n_agvs": 4, "total_steps": steps,
                 "total_completed": proposed.total_completed, "total_missions": proposed.mission_counter - 1,
                 "agv_diameter": 26, "pallet_diameter": 10, "pedestrian_radius": model.PEDESTRIAN_RADIUS,
                 "authoritative_positions": True, "navigation_version": 4,
                 "agv_clearance": model.AGV_CLEARANCE,
                 "navigation_obstacles": [z["name"] for z in model.zones_custom]},
        "zones": model.zones_custom, "chargers": model.chargers_custom,
        "stations": model.STATION_DEFINITIONS, "frames": proposed.history,
        "results": {"seed": seed, "steps": steps, "source": "m4_final_model.py",
                    "metrics": [{"name": name, "baseline": base_metrics[name], "proposed": value}
                                for name, value in metrics.items()]},
        "log": proposed.mission_manager.log
    })


def run_comparison(seed=42, steps=800, seed_count=1):
    seeds = [(seed + i) % 1_000_000 for i in range(seed_count)]
    data = run_single(seeds[0], steps)
    for next_seed in seeds[1:]:
        other = run_single(next_seed, steps)
        for total, row in zip(data["results"]["metrics"], other["results"]["metrics"]):
            assert total["name"] == row["name"]
            total["baseline"] += row["baseline"]
            total["proposed"] += row["proposed"]
    for row in data["results"]["metrics"]:
        row["baseline"] /= seed_count
        row["proposed"] /= seed_count
    data["results"].update(seed_count=seed_count, seeds=seeds, playback_seed=seeds[0])
    return data


jobs = {}
lock = threading.Lock()


def execute(job_id, seed, steps, seed_count, output):
    completed = 0
    total = 2 * seed_count * (steps + 1)
    def progress():
        nonlocal completed
        completed += 1
        if completed % 10 == 0 or completed == total:
            with lock:
                jobs[job_id].update(progress=min(completed / total, 1),
                                    phase="calculating", completed_steps=completed, total_steps=total)
    try:
        progress_context.callback = progress
        data = run_comparison(seed, steps, seed_count)
        with lock:
            jobs[job_id].update(phase="saving", progress=1)
        output.mkdir(parents=True, exist_ok=True)
        target = output / (job_id + ".json")
        temporary = target.with_suffix(".tmp")
        temporary.write_text(json.dumps(data, ensure_ascii=False, allow_nan=False), encoding="utf-8")
        temporary.replace(target)
        with lock:
            jobs[job_id].update(status="complete", phase="ready", progress=1, path=target)
    except Exception as exc:
        with lock:
            jobs[job_id].update(status="failed", error=str(exc))
    finally:
        progress_context.callback = None


class Handler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        # Unity or the launching terminal may close stderr while we keep serving.
        # HTTP responses must not fail just because access logging is unavailable.
        try:
            super().log_message(format, *args)
        except (OSError, ValueError):
            pass

    def respond(self, data, status=200):
        body = json.dumps(data, ensure_ascii=False, allow_nan=False).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/health":
            return self.respond({"status": "ready", "service": "warehouse-m4", "progress_version": 1})
        pieces = self.path.strip("/").split("/")
        if len(pieces) not in (2, 3) or pieces[0] != "runs":
            return self.respond({"error": "Not found"}, 404)
        with lock:
            job = dict(jobs.get(pieces[1], {}))
        if not job:
            return self.respond({"error": "Unknown run"}, 404)
        if len(pieces) == 3:
            if pieces[2] != "export" or job["status"] != "complete":
                return self.respond({"error": "Export not ready"}, 409)
            self.send_response(200)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(job["path"].stat().st_size))
            self.end_headers()
            with job["path"].open("rb") as source:
                while chunk := source.read(65536):
                    self.wfile.write(chunk)
            return
        return self.respond({k: v for k, v in job.items() if k != "path"})

    def do_POST(self):
        if self.path != "/runs":
            return self.respond({"error": "Not found"}, 404)
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if not 0 < length <= 4096:
                raise ValueError("Invalid body size")
            args = json.loads(self.rfile.read(length))
            if not isinstance(args, dict):
                raise ValueError("Expected an object")
            seed, steps = args.get("seed", 42), args.get("steps", 800)
            seed_count = args.get("seed_count", 1)
            if type(seed_count) is not int or not 1 <= seed_count <= 20:
                raise ValueError("seed_count: 1..20")
            if type(seed) is not int or type(steps) is not int or not 1 <= steps <= 5000 or not 0 <= seed <= 999999:
                raise ValueError("seed: 0..999999; steps: 1..5000")
        except (ValueError, TypeError):
            return self.respond({"error": "Invalid seed or steps"}, 400)
        with lock:
            if any(job["status"] == "running" for job in jobs.values()):
                return self.respond({"error": "A run is already active"}, 409)
            job_id = uuid.uuid4().hex
            jobs[job_id] = {"id": job_id, "status": "running", "error": "",
                            "progress": 0, "phase": "calculating"}
        threading.Thread(target=execute, args=(job_id, seed, steps, seed_count, self.server.output), daemon=True).start()
        self.respond({"id": job_id, "status": "running"}, 202)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--output", type=Path, default=Path(__file__).parent / "runs")
    args = parser.parse_args()
    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    server.output = args.output
    print(f"Warehouse API http://127.0.0.1:{args.port}", flush=True)
    server.serve_forever()
