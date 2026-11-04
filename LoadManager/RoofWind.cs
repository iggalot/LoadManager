using System;
using System.Collections.Generic;
using System.Linq;
using ASCE7WindLoadCalculator;
using BuildingModel.Geometry;

namespace LoadManager
{
    /// <summary>
    /// One wind case on the roof: the pressure at each point of the plan (psf, + down, − up), as the wind calculator gives it,
    /// with the internal pressure that lifts the roof most. The uplift pass of the load path (<see cref="GravityLoadPath.Uplift"/>)
    /// takes it across each tributary strip of the roof.
    /// </summary>
    public sealed class RoofWindCase
    {
        /// <summary>"Wind from the west, roof Cp giving the larger uplift".</summary>
        public string Name { get; set; }

        /// <summary>Where the pressures come from (the edition, figure and section).</summary>
        public string Source { get; set; }

        /// <summary>The pressure (psf) at a plan point (ft, the model's x east and y north): + down, − up.</summary>
        public Func<double, double, double> PressureAt { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// The wind calculator's roof pressures as <see cref="RoofWindCase"/>s on the model's plan. The calculator lays its zones out
    /// on the building's enclosing rectangle (L west to east, B south to north, from its south-west corner), the wind from the
    /// west along L and from the south along B (that one analysed on the building turned 90°); wind from the east and from the
    /// north are those mirrored. The roof's eaves (<see cref="BuildingModel.Model.Roof.Overhang"/>) carry the pressure on top of
    /// the zone they come out of, without internal pressure (the soffit is outside), and the windward eave also the pressure
    /// up on its soffit (ASCE 7 27.3.3: Cp 0.8; for components and cladding the figures' overhang coefficients, which include both faces).
    /// </summary>
    public static class RoofWind
    {
        private sealed class Zone
        {
            public List<Point2> Outline;
            public double Net, Top;
            public string Label;
        }

        /// <summary>
        /// The whole-building (MWFRS) cases: the wind from each of the four sides, each with both roof coefficients of Fig. 27.3-1
        /// (the one giving the larger suction and the other one ASCE 7 requires): eight cases. Empty when a calculator is missing.
        /// </summary>
        /// <param name="box">The building's enclosing rectangle (the model's envelope, the one the wind calculation was given).</param>
        public static List<RoofWindCase> Mwfrs(Rect2 box, WindLoadCalculator_Base alongLength, WindLoadCalculator_Base alongWidth)
        {
            var list = new List<RoofWindCase>();
            foreach (var (calc, sides) in new[] { (alongLength, new[] { "west", "east" }), (alongWidth, new[] { "south", "north" }) })
            {
                if (calc?.RoofAreaCalculator == null) continue;
                bool alongB = calc == alongWidth;
                double soffit = (calc as WindLoadCalculator_MWFRS)?.OverhangBottomPressure ?? 0;
                foreach (var (neg, caseName) in new[] { (true, "roof Cp giving the larger uplift"), (false, "the other roof Cp") })
                {
                    var pressures = neg ? calc.windPressureRoof_Neg_External : calc.windPressureRoof_Pos_External;
                    foreach (var side in sides)
                    {
                        bool mirror = side == "east" || side == "north";
                        var zones = Zones(calc, pressures, box, alongB, mirror, top: p => p.ExternalPressure);
                        if (zones.Count == 0) continue;
                        list.Add(new RoofWindCase
                        {
                            Name = $"Wind from the {side}, {caseName}",
                            Source = $"{calc.Edition?.Name ?? "ASCE 7"} Fig. 27.3-1 (MWFRS, directional), with internal pressure ±GCpi {calc.GetGCpi():0.00} taken to lift the roof; " +
                                     $"windward eave soffit Cp 0.8 (27.3.3), {soffit:0.0} psf up",
                            PressureAt = Field(box, zones, side, soffit),
                        });
                    }
                }
            }
            return list;
        }

        /// <summary>
        /// The components and cladding (C&amp;C) suction on the roof (Ch. 30 Part 1, zones 1 to 3 at the wind setup's roof component
        /// area, with internal pressure and the 16 psf minimum of 30.2.2), and on the eaves the figures' overhang pressures: one
        /// case, for any wind direction. Null when there is no cladding calculator.
        /// </summary>
        /// <param name="area">
        /// The effective wind area (sq ft, 26.2) the coefficients are read at: the component's, e.g. a rafter's span × the larger of
        /// its spacing and a third of its span. 0: the wind setup's roof component area (the calculator's own tables).
        /// </param>
        public static RoofWindCase Cladding(Rect2 box, WindLoadCalculator_Base cc, double area = 0)
        {
            if (cc?.RoofAreaCalculator == null) return null;
            var curves = cc.extGCpCurve_Roof;
            List<Zone> zones;
            if (area > 0 && curves?.RoofCurves_Neg != null)
            {
                // the calculator's own coefficients and pressure terms, read at this area (as it does at its component area)
                double qhKd = cc.CalculateDynamicWindPressure(cc.buildingData.MeanRoofHeight) * cc.PressureKd, gcpi = cc.GetGCpi();
                var wallPos = cc.extGCpCurve_Wall?.WallCurves_Pos;
                bool roofPlusWall = cc.Edition?.Version == ASCE7_Versions.ASCE_VER_7_22;
                double Min(double p) => p < 0 && -p < WindLoadCalculator_CC.MinimumNetPressure ? -WindLoadCalculator_CC.MinimumNetPressure : p;
                var table = new Dictionary<int, PressureData>();
                var eaves = new Dictionary<int, double>();
                foreach (var kv in cc.RoofAreaCalculator.effWindAreas)
                {
                    string label = kv.Value.Label_Full;
                    if (!curves.RoofCurves_Neg.TryGetValue(label, out var neg)) continue;
                    double gcp = neg.Evaluate(area);
                    table[kv.Key] = new PressureData { AreaID = kv.Key, GCp = gcp, ExternalPressure = qhKd * gcp, InternalPressure = qhKd * gcpi, MinimumNet = WindLoadCalculator_CC.MinimumNetPressure };
                    // the eave: the figure's overhang coefficient (both faces, no internal pressure); ASCE 7-22 without one, the roof's
                    // plus the wall's below it (as the calculator does)
                    double? o = curves.OverhangCurves != null && curves.OverhangCurves.TryGetValue(label, out var oc) ? oc.Evaluate(area)
                              : roofPlusWall && wallPos != null && wallPos.TryGetValue(label.StartsWith("Zone3", StringComparison.Ordinal) ? "Zone5" : "Zone4", out var wc) ? gcp - wc.Evaluate(area)
                              : (double?)null;
                    eaves[kv.Key] = Min(qhKd * (o ?? gcp));
                }
                zones = Zones(cc, table, box, false, false, top: p => eaves[p.AreaID]);
            }
            else
            {
                area = (cc as WindLoadCalculator_CC)?.RoofComponentArea ?? 0;
                zones = Zones(cc, cc.windPressureRoof_Neg_External, box, false, false,
                    top: p => cc.windPressureOverhang_External.TryGetValue(p.AreaID, out var o) ? o.NetPressure : p.ExternalPressure);
            }
            if (zones.Count == 0) return null;
            return new RoofWindCase
            {
                Name = $"Components and cladding, roof suction at {area:0} sq ft",
                Source = $"{cc.Edition?.Name ?? "ASCE 7"} Ch. 30 Part 1 roof zones 1 to 3 (C&C) at an effective wind area of {area:0} sq ft (26.2), " +
                         $"with internal pressure ±GCpi {cc.GetGCpi():0.00} and the {WindLoadCalculator_CC.MinimumNetPressure:0} psf minimum (30.2.2); eaves at the overhang coefficients",
                PressureAt = Field(box, zones, null, 0),
            };
        }

        /// <summary>The zones on the model's plan: each outline moved (and turned, mirrored) there, with its net and eave-top pressures.</summary>
        private static List<Zone> Zones(WindLoadCalculator_Base calc, Dictionary<int, PressureData> pressures, Rect2 box, bool alongB, bool mirror,
            Func<PressureData, double> top)
        {
            double L = box.Width, B = box.Height;
            Point2 Map(Vec2 p)
            {
                // the along-width calculation's x runs south to north, its y west to east
                double x = alongB ? p.Y : p.X, y = alongB ? p.X : p.Y;
                if (mirror) { if (alongB) y = B - y; else x = L - x; }
                return new Point2(box.X0 + x, box.Y0 + y);
            }
            var list = new List<Zone>();
            foreach (var kv in calc.RoofAreaCalculator.effWindAreas)
            {
                if (pressures == null || !pressures.TryGetValue(kv.Key, out var p)) continue;
                list.Add(new Zone
                {
                    Outline = kv.Value.OuterBoundary.Select(Map).ToList(),
                    Net = Lifting(p),
                    Top = top(p),
                    Label = kv.Value.Label_Full,
                });
            }
            return list;
        }

        /// <summary>
        /// The net pressure with the internal pressure that lifts the roof most (+GCpi, pushing up from inside): external − internal;
        /// an upward one less than a minimum (C&amp;C, 30.2.2) is raised to it.
        /// </summary>
        public static double Lifting(PressureData p)
        {
            double net = p.ExternalPressure - Math.Abs(p.InternalPressure);
            return p.MinimumNet > 0 && net < 0 && -net < p.MinimumNet ? -p.MinimumNet : net;
        }

        /// <summary>
        /// The pressure at a plan point: in the zone it is in (on a border, the nearest); past the building's rectangle (on an eave)
        /// the eave-top pressure of the zone it comes out of, less the soffit pressure on the windward side.
        /// </summary>
        private static Func<double, double, double> Field(Rect2 box, List<Zone> zones, string windward, double soffit) => (x, y) =>
        {
            double cx = Math.Max(box.X0, Math.Min(box.X1, x)), cy = Math.Max(box.Y0, Math.Min(box.Y1, y));
            bool eave = Math.Abs(cx - x) > 1e-9 || Math.Abs(cy - y) > 1e-9;
            var at = new Point2(cx, cy);
            var z = zones.FirstOrDefault(q => Inside(q.Outline, at)) ?? zones.OrderBy(q => DistanceTo(q.Outline, at)).First();
            if (!eave) return z.Net;
            bool upwind = windward == "west" ? x < box.X0 : windward == "east" ? x > box.X1 : windward == "south" ? y < box.Y0 : windward == "north" && y > box.Y1;
            return z.Top - (upwind ? soffit : 0);
        };

        private static bool Inside(List<Point2> poly, Point2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                    p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                    inside = !inside;
            return inside;
        }

        private static double DistanceTo(List<Point2> poly, Point2 p)
        {
            double best = double.MaxValue;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Point2 a = poly[j], b = poly[i];
                double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
                double t = len2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
                double ex = a.X + t * dx - p.X, ey = a.Y + t * dy - p.Y;
                best = Math.Min(best, Math.Sqrt(ex * ex + ey * ey));
            }
            return best;
        }
    }
}
