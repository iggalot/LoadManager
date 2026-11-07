using System;
using System.Collections.Generic;
using System.Linq;
using ASCE7WindLoadCalculator.ModelAdapter;
using BuildingModel.Model;

namespace LoadManager
{
    /// <summary>
    /// A flag for the foundation design, not a fault: a post, column or stretch of footing the combined loads lift. Uplift on a
    /// foundation is allowed; it needs a footing designed for it, so the foundation designer must see where.
    /// </summary>
    public sealed class FoundationProblem
    {
        /// <summary>The wall whose footing it is, or the point ("C1", "BM1 via W7").</summary>
        public string Id { get; set; }
        public string Message { get; set; }
    }

    /// <summary>The Load Manager's foundation work on a project as it is now (<see cref="FoundationCheck.Run"/>).</summary>
    public sealed class FoundationCheckResult
    {
        /// <summary>Why the foundation is not checked (no gravity loads yet), or null.</summary>
        public string NotChecked { get; set; }
        /// <summary>The wind it took (the wind calculator's, <see cref="WindCheck"/>).</summary>
        public WindCheckResult Wind { get; set; }
        /// <summary>Why there is no roof wind (no wind set up, it cannot be calculated), or null.</summary>
        public string WindWhy { get; set; }
        public List<RoofWindCase> WindCases { get; set; }
        public Func<double, RoofWindCase> Cladding { get; set; }
        public GravityLoadPath.Result LoadPath { get; set; }
        public WindUplift Uplift { get; set; }
        /// <summary>The shear walls' forces it combined (given, or the file's).</summary>
        public LateralSection Lateral { get; set; }
        public FoundationAnalysis Analysis { get; set; }
        public List<FoundationProblem> Problems { get; } = new List<FoundationProblem>();
    }

    /// <summary>
    /// The Load Manager's foundation loads with no window: the gravity load path, the roof wind's uplift down it (the wind
    /// calculator's cases for the building as it is), and the shear walls' hold-down forces, combined
    /// (<see cref="FoundationAnalysis.Run"/>). The Load Manager's window uses the same steps. The hub runs it after the shear
    /// walls' own check, with their forces as they are now, so the foundation is checked against everything current. Only reads.
    /// </summary>
    public static class FoundationCheck
    {
        /// <summary>The roof wind for the building: the wind calculator's cases and its cladding. Returns why there is none, or null.</summary>
        public static string RoofWindOf(Building b, WindCheckResult wind, out List<RoofWindCase> cases, out Func<double, RoofWindCase> cladding)
        {
            cases = null;
            cladding = null;
            if (wind == null || wind.NotChecked != null)
                return "This project has no wind setup (the wind calculator's inputs in the file), so there is no roof wind: set it up in the wind or shear wall app.";
            if (!wind.Calculated)
                return "The wind calculator could not find the roof pressures for this building: " + string.Join(" ", wind.Problems);
            var box = b.Envelope().Bounds;
            // the wind calculator's own wind cases, each as it made it (its setup's procedure and Fig. 27.3-8 exception)
            cases = RoofWind.Cases(box, wind.Cases, wind.Analysis.MwfrsAlongLength?.Edition?.Name);
            var cc = wind.Analysis.Cladding;
            cladding = a => RoofWind.Cladding(box, cc, a);
            return null;
        }

        /// <summary>
        /// The load path, the uplift and the combined foundation for these loads and this wind. <paramref name="why"/> is
        /// set when the uplift could not be found.
        /// </summary>
        public static FoundationAnalysis Analyze(Building b, LoadSection s, List<RoofWindCase> cases, Func<double, RoofWindCase> cladding,
                                                 LateralSection lateral, DesignMethod method,
                                                 out GravityLoadPath.Result path, out WindUplift uplift, ref string why)
        {
            path = GravityLoadPath.Run(b, s);
            try { uplift = cases == null && cladding == null ? null : WindUplift.Run(b, s, cases, cladding); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                uplift = null;
                why = "The wind uplift could not be found: " + ex.Message;
            }
            return FoundationAnalysis.Run(b, s, uplift, lateral, method);
        }

        /// <summary>
        /// The foundation for <paramref name="b"/> as it is: its loads, the wind found now (<paramref name="wind"/>, or found
        /// here), and <paramref name="lateral"/> (null: the shear walls' forces saved in the file).
        /// </summary>
        public static FoundationCheckResult Run(Building b, LateralSection lateral = null, DesignMethod method = DesignMethod.Asd, WindCheckResult wind = null)
        {
            var r = new FoundationCheckResult();
            if (b == null) { r.NotChecked = "no project"; return r; }
            var s = LoadSection.Read(b);
            if (s == null || s.IsEmpty) { r.NotChecked = "no gravity loads entered"; return r; }
            r.Wind = wind ?? WindCheck.Run(b);
            r.WindWhy = RoofWindOf(b, r.Wind, out var cases, out var cladding);
            r.WindCases = cases;
            r.Cladding = cladding;
            r.Lateral = lateral ?? LateralSection.Read(b);
            string why = r.WindWhy;
            r.Analysis = Analyze(b, s, cases, cladding, r.Lateral, method, out var path, out var uplift, ref why);
            r.WindWhy = why;
            r.LoadPath = path;
            r.Uplift = uplift;
            r.Problems.AddRange(Problems(r.Analysis));
            return r;
        }

        /// <summary>
        /// Where the combined loads lift the foundation, for the foundation designer (a footing designed for uplift): a post or
        /// column with no hold-down (a shear wall's end is carried by its own), or a stretch of footing, in some combination.
        /// </summary>
        public static List<FoundationProblem> Problems(FoundationAnalysis f)
        {
            var list = new List<FoundationProblem>();
            if (f == null) return list;
            foreach (var w in f.Walls)
                if (w.Line != null && w.Line.Lifts)
                    list.Add(new FoundationProblem { Id = w.Id, Message = $"The footing of {w.Id} lifts {-w.Line.Min:N0} plf ({w.Line.MinBy?.Name}). For the foundation design: a footing designed for uplift." });
            foreach (var p in f.AllPoints)
                if (p.Envelope != null && p.Envelope.Lifts && p.Kind != FoundationPointKind.Lateral && !p.HasLateral)
                    list.Add(new FoundationProblem { Id = p.From, Message = $"{p.From} lifts {-p.Envelope.Min:N0} lb ({p.Envelope.MinBy?.Name}) with no hold-down there. For the foundation design: a footing (and connection) designed for uplift." });
            return list;
        }
    }
}
