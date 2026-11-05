using System;
using System.Collections.Generic;
using System.Linq;
using ASCE7WindLoadCalculator;
using BuildingModel.Geometry;

namespace LoadManager
{
    /// <summary>
    /// One wind case on the roof: the pressure at each point of the plan (psf, + down, − up), as the wind calculator gives it,
    /// with that case's internal pressure (the cladding case: the internal pressure that lifts the roof most). The uplift pass of the load path (<see cref="GravityLoadPath.Uplift"/>)
    /// takes it across each tributary strip of the roof.
    /// </summary>
    public sealed class RoofWindCase
    {
        /// <summary>The wind calculator's case name ("Case 1, from W, roof Case A, +GCpi"), or the cladding case's.</summary>
        public string Name { get; set; }

        /// <summary>Where the pressures come from (the edition, figure and section).</summary>
        public string Source { get; set; }

        /// <summary>The side the wind comes from, as the calculator's case has it: "W", "E", "S", "N", "SW", ... (null: any, e.g. the cladding case).</summary>
        public string From { get; set; }

        /// <summary>The pressure (psf) at a plan point (ft, the model's x east and y north): + down, − up.</summary>
        public Func<double, double, double> PressureAt { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// The wind calculator's roof pressures as <see cref="RoofWindCase"/>s on the model's plan: its own wind cases as it made them
    /// (<see cref="Cases"/>), and the components and cladding suction. The calculator lays its zones out on the building's
    /// enclosing rectangle (L west to east, B south to north, from its south-west corner). The roof's eaves (<see cref="BuildingModel.Model.Roof.Overhang"/>) carry the pressure on top of
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
        /// The whole-building (MWFRS) wind on the roof: one case for each of the wind calculator's own wind cases
        /// (<see cref="WindCases.For"/>), in its order and under its name, each with that case's own roof zones and its own sign
        /// of the internal pressure (net = external − internal, as the case has them). Nothing is chosen, paired or built here.
        /// The eaves: the top takes the external pressure of the zone it comes out of (the soffit is outside, no internal
        /// pressure); a windward eave of the case's wind also the pressure up on its soffit (the case's: 27.3.3 Cp 0.8, or 28.3.3
        /// GCp 0.7). A case without roof zones (the 16/8 psf minimum: its 8 psf is horizontal, on the roof's projection) puts
        /// no pressure on the roof.
        /// </summary>
        /// <param name="box">The building's enclosing rectangle (the model's envelope, the one the wind calculation was given);
        /// the calculator's plan runs from its south-west corner, x east along the length, y north.</param>
        public static List<RoofWindCase> Cases(Rect2 box, IEnumerable<ASCE7WindLoadCalculator.WindCase> cases, string edition = null)
        {
            var list = new List<RoofWindCase>();
            // cases with the same zone outlines (the ±GCpi pair, the two torsions, ...) share one locator, so which zone a point
            // is in is found once for all of them; each case keeps its own pressures
            var locators = new Dictionary<string, Locator>();
            foreach (var c in cases ?? Enumerable.Empty<ASCE7WindLoadCalculator.WindCase>())
            {
                var zones = c.Zones.Where(z => z.Kind == SurfaceKind.Roof && z.Outline != null && z.Outline.Count >= 3)
                    .Select(z => new Zone
                    {
                        Outline = z.Outline.Select(p => new Point2(box.X0 + p.X, box.Y0 + p.Y)).ToList(),
                        Net = z.NetPsf, Top = z.ExternalPsf, Label = z.Label,
                    }).ToList();
                string layout = string.Join(";", zones.Select(z => string.Join(" ", z.Outline.Select(p => FormattableString.Invariant($"{p.X:R},{p.Y:R}")))));
                if (zones.Count > 0 && !locators.ContainsKey(layout)) locators[layout] = new Locator(zones.Select(z => z.Outline).ToList());
                string from = c.From ?? "";
                var soffit = new Dictionary<string, double>
                {
                    ["west"] = from.Contains("W") ? c.SoffitPsfAlongLength : 0, ["east"] = from.Contains("E") ? c.SoffitPsfAlongLength : 0,
                    ["south"] = from.Contains("S") ? c.SoffitPsfAlongWidth : 0, ["north"] = from.Contains("N") ? c.SoffitPsfAlongWidth : 0,
                };
                string chapter = c.Procedure == LateralProcedure.Envelope ? "Ch. 28 (envelope)" : "Ch. 27 (directional)";
                list.Add(new RoofWindCase
                {
                    Name = c.Name,
                    From = c.From,
                    Source = c.IsMinimumCase
                        ? $"{edition ?? "ASCE 7"} the wind calculator's separate minimum case: 8 psf horizontal on the roof's projection, no pressure on the roof itself"
                        : $"{edition ?? "ASCE 7"} {chapter}, the wind calculator's case \"{c.Name}\": its own roof zones and internal pressure ({(c.InternalSign > 0 ? "+" : c.InternalSign < 0 ? "−" : "")}GCpi); " +
                          $"windward eave soffit {Math.Max(c.SoffitPsfAlongLength, c.SoffitPsfAlongWidth):0.0} psf up ({(c.Procedure == LateralProcedure.Envelope ? "28.3.3" : "27.3.3")})",
                    PressureAt = zones.Count == 0 ? (Func<double, double, double>)((x, y) => 0) : Field(box, zones, soffit, locators[layout]),
                });
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
                PressureAt = Field(box, zones, null),
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
        /// <param name="soffit">The pressure up on the soffit of each side's eave ("west", "east", "south", "north"; psf): the
        /// windward ones of the case; null: none.</param>
        /// <param name="locator">Which zone a point is in (shared by cases with the same outlines); null: one of its own.</param>
        private static Func<double, double, double> Field(Rect2 box, List<Zone> zones, Dictionary<string, double> soffit, Locator locator = null)
        {
            var at = locator ?? new Locator(zones.Select(z => z.Outline).ToList());
            var net = zones.Select(z => z.Net).ToArray();
            var top = zones.Select(z => z.Top).ToArray();
            double Up(string side) => soffit != null && soffit.TryGetValue(side, out var s) ? s : 0;
            double west = Up("west"), east = Up("east"), south = Up("south"), north = Up("north");
            return (x, y) =>
            {
                double cx = Math.Max(box.X0, Math.Min(box.X1, x)), cy = Math.Max(box.Y0, Math.Min(box.Y1, y));
                bool eave = Math.Abs(cx - x) > 1e-9 || Math.Abs(cy - y) > 1e-9;
                int i = at.Find(cx, cy);
                if (!eave) return net[i];
                double up = x < box.X0 ? west : x > box.X1 ? east : y < box.Y0 ? south : north;
                return top[i] - up;
            };
        }

        /// <summary>
        /// Which of a set of zone outlines a plan point is in: the first that holds it, else (on a border) the nearest. Each point
        /// is found once and remembered (the load path asks for the same points in every case); safe to use from several threads.
        /// </summary>
        private sealed class Locator
        {
            private readonly List<Point2>[] outlines;
            private readonly Rect2[] bounds;
            private readonly System.Collections.Concurrent.ConcurrentDictionary<(double, double), int> found =
                new System.Collections.Concurrent.ConcurrentDictionary<(double, double), int>();

            public Locator(List<List<Point2>> outlines)
            {
                this.outlines = outlines.ToArray();
                bounds = this.outlines.Select(o => new Rect2(o.Min(p => p.X), o.Min(p => p.Y), o.Max(p => p.X), o.Max(p => p.Y))).ToArray();
            }

            public int Find(double x, double y) => found.GetOrAdd((x, y), k => Search(k.Item1, k.Item2));

            private int Search(double x, double y)
            {
                var p = new Point2(x, y);
                for (int i = 0; i < outlines.Length; i++)
                {
                    var b = bounds[i];
                    if (x >= b.X0 && x <= b.X1 && y >= b.Y0 && y <= b.Y1 && Inside(outlines[i], p)) return i;
                }
                int best = 0; double d = double.MaxValue;
                for (int i = 0; i < outlines.Length; i++)
                {
                    double di = DistanceTo(outlines[i], p);
                    if (di < d) { d = di; best = i; }
                }
                return best;
            }
        }

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
