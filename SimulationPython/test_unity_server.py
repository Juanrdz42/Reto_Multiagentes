import math
import unittest
import tempfile
from pathlib import Path
from unittest.mock import patch
from io import BytesIO
import unity_server


class ExportTests(unittest.TestCase):
    def test_background_progress_and_atomic_export(self):
        updates = []
        class Job(dict):
            def update(self, **values):
                super().update(**values)
                updates.append(dict(self))
        unity_server.jobs["progress-test"] = Job(status="running")
        try:
            with tempfile.TemporaryDirectory() as folder:
                unity_server.execute("progress-test", 42, 40, 2, Path(folder))
                job = unity_server.jobs["progress-test"]
                self.assertEqual(job["status"], "complete", job)
                self.assertTrue(job["path"].exists())
                self.assertFalse(job["path"].with_suffix(".tmp").exists())
                progress = [u["progress"] for u in updates]
                self.assertEqual(progress, sorted(progress))
                self.assertTrue(any(0 < p < 1 for p in progress))
                self.assertEqual(progress[-1], 1)
                self.assertEqual(job["completed_steps"], 164)
                self.assertIsNone(unity_server.progress_context.callback)
        finally:
            del unity_server.jobs["progress-test"]

    def test_response_survives_closed_launcher_stderr(self):
        handler = object.__new__(unity_server.Handler)
        handler.request_version = "HTTP/1.1"
        handler.requestline = "GET /health HTTP/1.1"
        handler.client_address = ("127.0.0.1", 12345)
        handler.wfile = BytesIO()
        with patch("sys.stderr") as stderr:
            stderr.write.side_effect = BrokenPipeError("Launcher closed")
            handler.respond({"status": "ready"})
        self.assertIn(b"200 OK", handler.wfile.getvalue())
        self.assertIn(b'{"status": "ready"}', handler.wfile.getvalue())

    def test_body_clearance_for_complete_runs(self):
        for seed in (42, 1, 7):
            data = unity_server.run_comparison(seed=seed, steps=800)
            self.assertGreater(data["meta"]["total_completed"], 0)
            for frame in data["frames"]:
                for agv in frame["agvs"]:
                    self.assertGreater(agv["battery"], 0, (seed, frame["t"], agv))
                    if agv["battery"] < 30:
                        self.assertEqual(agv["state"], "CHARGING")
                    if agv["charge_phase"] == "CHARGING":
                        self.assertTrue(any(unity_server.model.np.linalg.norm(
                            unity_server.model.np.array(agv["pos"]) - charger["pos"]) < 0.001
                            for charger in data["chargers"]))
            if seed == 42:
                self.assertGreaterEqual(data["meta"]["total_completed"], 12)
                for i in range(4):
                    stopped = 0
                    for a, b in zip(data["frames"], data["frames"][1:]):
                        first, second = a["agvs"][i], b["agvs"][i]
                        stopped = stopped + 1 if first["state"] in ("TRANSPORTING", "MOVING_TO_PALLET") and first["pos"] == second["pos"] else 0
                        self.assertLess(stopped, 40, (first["id"], a["t"]))
            for a, b in zip(data["frames"], data["frames"][1:]):
                for first, second in zip(a["agvs"], b["agvs"]):
                    self.assertTrue(unity_server.model.segment_clear_of_obstacles(first["pos"], second["pos"]),
                                    (seed, a["t"], first["id"]))
        self.assertFalse(unity_server.model.segment_clear_of_obstacles([140, 190], [260, 230]))

    def test_recharge_suspends_and_resumes_mission(self):
        m = unity_server.model.run_simulation("proposed", steps=160, seed=42,
                                             routing=unity_server.model.ROUTING_ASTAR)
        agv = next(a for a in m.agvs if a.current_mission is not None)
        mission = agv.current_mission
        previous_state = agv.state
        agv.battery = 29
        self.assertTrue(agv.needs_recharge())
        self.assertTrue(agv.go_to_recharge(m.chargers))
        self.assertIs(agv.current_mission, mission)
        self.assertEqual(agv.suspended_state, previous_state)
        self.assertGreater(agv.battery, unity_server.model.route_energy(agv.pos, agv.path))
        agv.pos = unity_server.model.np.array(agv.charger["pos"], dtype=float)
        agv.path = []
        agv.battery = 79
        m.step()
        self.assertIs(agv.current_mission, mission)
        self.assertEqual(agv.state, previous_state)
        self.assertGreaterEqual(agv.battery, 80)
        agv.battery = 29
        # Una cola de cargadores no debe consumir batería ni abandonar la misión.
        for c in m.chargers:
            c["occupied_by"] = "test"
        agv.charger = None
        agv.path = []
        before = agv.pos.copy()
        self.assertFalse(agv.go_to_recharge(m.chargers))
        self.assertEqual(agv.charge_phase, "WAITING")
        self.assertTrue(unity_server.model.np.array_equal(agv.pos, before))
        self.assertEqual(agv.battery, 29)

    def test_average_keeps_first_seed_history(self):
        first = unity_server.run_single(42, 160)
        second = unity_server.run_single(43, 160)
        averaged = unity_server.run_comparison(42, 160, 2)
        self.assertEqual(averaged["frames"], first["frames"])
        self.assertEqual(averaged["meta"], first["meta"])
        self.assertEqual(averaged["results"]["seeds"], [42, 43])
        for a, b, row in zip(first["results"]["metrics"], second["results"]["metrics"], averaged["results"]["metrics"]):
            self.assertAlmostEqual(row["baseline"], (a["baseline"] + b["baseline"]) / 2)
            self.assertAlmostEqual(row["proposed"], (a["proposed"] + b["proposed"]) / 2)

    def test_complete_history_and_metrics(self):
        data = unity_server.run_comparison(seed=42, steps=160)
        self.assertEqual([f["t"] for f in data["frames"]], list(range(161)))
        self.assertEqual(len(data["results"]["metrics"]), 13)
        for row in data["results"]["metrics"]:
            self.assertTrue(math.isfinite(row["baseline"]))
            self.assertTrue(math.isfinite(row["proposed"]))
        for frame in data["frames"]:
            self.assertEqual(len({a["id"] for a in frame["agvs"]}), 4)
            for pallet in frame["pallets"]:
                self.assertIn("removed", pallet)
                self.assertIn("storage_level", pallet)
                if pallet["state"] != "SIENDO_TRANSPORTADO" and not pallet["removed"]:
                    self.assertTrue(pallet["storage_zone"], pallet)


if __name__ == "__main__":
    unittest.main()
