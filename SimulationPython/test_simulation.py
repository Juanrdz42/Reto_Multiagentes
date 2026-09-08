"""Regresiones de rutas: ejecutar con python -m unittest discover -s SimulationPython."""
import unittest
import m4_simulation as sim


class RouteTests(unittest.TestCase):
    def test_original_corner_cut_is_blocked(self):
        self.assertFalse(sim.segment_clear_of_obstacles((242.5, 197.5), (252.5, 202.5)))
        self.assertFalse(sim.segment_clear_of_obstacles((242.5, 197.5), (252.5, 197.5)))
        self.assertTrue(sim.segment_clear_of_obstacles((232.5, 177.5), (277.5, 177.5)))

    def test_detour_cannot_cross_rack_with_free_endpoints(self):
        self.assertFalse(sim.segment_clear_of_obstacles((140, 210), (260, 210)))
        self.assertTrue(sim.segment_clear_of_obstacles((130, 180), (270, 180)))

    def test_service_points_and_grid_allow_the_larger_body(self):
        self.assertEqual(sim.AGV_DIAMETER, 26)
        self.assertEqual(sim.PALLET_DIAMETER, 10)
        points = [s["pos"] for s in sim.STATION_DEFINITIONS]
        points += [c["pos"] for c in sim.chargers_custom]
        points += [sim.edge_point(z, "east") for z in sim.zones_custom if z["name"].startswith("Prod Line")]
        for point in points:
            self.assertTrue(sim.segment_clear_of_obstacles(point, point), point)
            x, y = sim.world_to_grid(point)
            self.assertEqual(sim.NAV_GRID[y, x], 0, point)

    def test_exported_runs_keep_complete_steps_and_clearance(self):
        for seed in (42, 1):
            with self.subTest(seed=seed):
                model = sim.run_simulation(sim.STRATEGY_PROPOSED, steps=800, seed=seed)
                self.assertEqual([f['t'] for f in model.history], list(range(801)))
                self.assertGreater(model.total_completed, 0)
                valid_zones = {z["name"] for z in sim.zones_custom}
                for frame in model.history:
                    occupied = []
                    for pallet in frame["pallets"]:
                        if pallet["removed"]:
                            self.assertIsNone(pallet["storage_zone"])
                        elif pallet["state"] == sim.P_BEING_TRANSPORTED:
                            self.assertIsNone(pallet["storage_zone"])
                        else:
                            self.assertIn(pallet["storage_zone"], valid_zones)
                            self.assertIn(pallet["storage_level"], (0, 1))
                            occupied.append(pallet["storage_zone"])
                    self.assertEqual(len(occupied), len(set(occupied)), (seed, frame["t"], occupied))
                for a, b in zip(model.history, model.history[1:]):
                    for agv, following in zip(a['agvs'], b['agvs']):
                        self.assertEqual(agv['id'], following['id'])
                        self.assertTrue(sim.segment_clear_of_obstacles(agv['pos'], following['pos']),
                                        (seed, a['t'], agv['id']))
                        x, y = following['pos']
                        self.assertTrue(sim.AGV_CLEARANCE <= x <= sim.WIDTH - sim.AGV_CLEARANCE
                                        and sim.AGV_CLEARANCE <= y <= sim.HEIGHT - sim.AGV_CLEARANCE)


if __name__ == '__main__':
    unittest.main()
