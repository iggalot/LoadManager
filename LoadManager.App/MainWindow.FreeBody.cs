using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using BuildingModel.Geometry;
using BuildingModel.Sketch;

namespace LoadManager.App
{
    public sealed partial class MainWindow
    {
        /// <summary>The members whose free body is drawn in 3D (empty: the foundation).</summary>
        private List<string> fbIds = new List<string>();
        private FreeBody fb;
        private List<LoadCombination> fbCombos = new List<LoadCombination>();
        /// <summary>The combination the free body is drawn in (null: service loads, D + L + Lr + S as entered).</summary>
        private LoadCombination fbCombo;
        /// <summary>The members picked (Ctrl or Shift + click in the whole building) for a free body of them together.</summary>
        private readonly HashSet<string> fbPicked = new HashSet<string>();
        /// <summary>
        /// Draw the pairs inside the body too (both halves of each connection between two of its members; they cancel): the
        /// visual check that everything is accounted for. Off: the cleaner drawing, the body's W, I, S and E only.
        /// </summary>
        private bool fbShowInside { get => sketch.ShowInternalForces; set => sketch.SetInternalForces(value); }

        /// <summary>The own weight's colour on a free body: green (loads applied are blue, the supports red, the cut purple).</summary>
        private static readonly Color WeightColor = Color.FromRgb(0x2E, 0x7D, 0x32);
        /// <summary>The internal forces at the cut: purple. The pairs inside the body: grey.</summary>
        private static readonly Color CutColor = Color.FromRgb(0x7B, 0x1F, 0xA2);
        private static readonly Color InsideColor = Color.FromRgb(0x90, 0x90, 0x90);

        private void Picked3D(string id)
        {
            if (mode != Mode.Foundation3D || string.IsNullOrEmpty(id)) return;
            bool add = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
            if (fb == null && add)
            {
                // picking several for one free body: Ctrl or Shift + click each, then click (or Free body of them)
                if (!fbPicked.Remove(id)) fbPicked.Add(id);
                view3D.HighlightIds = fbPicked;
                status.Text = fbPicked.Count == 0 ? "None picked." : $"Picked for one free body: {string.Join(", ", fbPicked)}. Click one more, or Free body of them in the side panel.";
                RefreshSide();
                view3D.Show(B);
                return;
            }
            if (fb != null && add)
            {
                // a member of the body: Ctrl + click takes it out
                var rest = fbIds.Where(x => x != id).ToList();
                if (rest.Count > 0 && rest.Count < fbIds.Count) OpenFreeBody(rest);
                return;
            }
            if (fb != null && fbIds.Count == 1 && fbIds[0] == id) return;
            var ids = fb == null && fbPicked.Count > 0 ? fbPicked.Concat(new[] { id }).Distinct().ToList() : new List<string> { id };
            OpenFreeBody(ids);
        }

        private void OpenFreeBody(IEnumerable<string> ids)
        {
            var list = ids.Distinct().ToList();
            var body = FreeBody.OfBody(B, LoadSection.Read(B), loadPath, list, lateral);
            if (body == null) { status.Text = $"{string.Join(", ", list)}: not a wall, beam or column the load path knows."; return; }
            fbIds = body.Members.Select(m => m.Id).ToList();
            fb = body;
            fbPicked.Clear();
            view3D.HighlightIds = null;
            fbCombos = fb.Combinations(method);
            fbCombo = fbCombos.FirstOrDefault();
            view3D.OnlyIds = new HashSet<string>(fbIds);
            view3D.DrawOnly = !(AllWalls && fbFraming);
            legend3D.Visibility = Visibility.Collapsed;
            Title3D();
            RefreshSide();
            view3D.Show(B);
        }

        private void OpenFreeBody(string id) => OpenFreeBody(new[] { id });

        /// <summary>The body is walls only (their framing can be drawn in place of the see-through blocks).</summary>
        private bool AllWalls => fb != null && fb.Members.All(m => m.Kind == "wall");

        private void CloseFreeBody()
        {
            if (fb == null) return;
            fbIds = new List<string>(); fb = null; fbCombos = new List<LoadCombination>(); fbCombo = null;
            view3D.OnlyIds = null;
            view3D.Only = null;
            view3D.DrawOnly = true;
            legend3D.Visibility = view3D.Visibility;
            Title3D();
            RefreshSide();
            view3D.Show(B);
        }

        /// <summary>After a change of the loads or the building: the free body again (of the members still there), or back to the foundation.</summary>
        private void RefreshFreeBody()
        {
            if (fb == null) return;
            var body = FreeBody.OfBody(B, LoadSection.Read(B), loadPath, fbIds, lateral);
            if (body == null) { fbIds = new List<string>(); fb = null; view3D.OnlyIds = null; fbCombo = null; return; }
            fb = body;
            fbIds = body.Members.Select(m => m.Id).ToList();
            view3D.OnlyIds = new HashSet<string>(fbIds);
            string was = fbCombo?.Name;
            fbCombos = fb.Combinations(method);
            fbCombo = fbCombos.FirstOrDefault(c => c.Name == was) ?? fbCombos.FirstOrDefault();
        }

        private void StepFreeBody(int by)
        {
            if (fbCombos.Count == 0) return;
            int i = fbCombo == null ? -1 : fbCombos.IndexOf(fbCombo);
            i = ((i + by) % fbCombos.Count + fbCombos.Count) % fbCombos.Count;
            ShowFreeBodyCombo(fbCombos[i]);
        }

        private void ShowFreeBodyCombo(LoadCombination c)
        {
            fbCombo = c;
            Title3D();
            if (comboList != null)
            {
                settingList = true;
                comboList.SelectedIndex = fbCombos.IndexOf(c);
                if (comboList.SelectedItem != null) comboList.ScrollIntoView(comboList.SelectedItem);
                settingList = false;
            }
            RefreshFreeBodyTable();
            view3D.Show(B);
        }

        private string FreeBodyName => fb == null ? "" : fb.Kind == "body" ? $"{fb.Id}" : $"{char.ToUpper(fb.Kind[0])}{fb.Kind.Substring(1)} {fb.Id}";

        private void FreeBodyTitle()
        {
            title3D.Text = $"FREE BODY of {FreeBodyName}: " + (fbCombo == null ? "service loads" : fbCombo.Name);
            sub3D.Text = "The selected members, alone, cut from the rest of the structure (WISE): E the loads applied to it in blue; W its own weight in green; " +
                         "S the foundation's reactions in red; I in purple, where it is cut, the reactions of the members connected to it that are not selected. " +
                         (fbShowInside ? "The pairs between two selected members are drawn in grey, both halves: they cancel. " : "The pairs between two selected members cancel and are not drawn. ") +
                         "Distributed loads are rows of light arrows, point loads single full-colour ones; the numbers are in the side panel." +
                         (fbCombo == null ? "" : $" {fbCombo.Equation}.");
        }

        // ------------------------------------------------------------------ the side panel

        /// <summary>In the whole building in 3D: how to draw a free body, and the members picked for one.</summary>
        private void FreeBodyPickSide()
        {
            side.Children.Add(Head("Free body"));
            side.Children.Add(Para("Click a wall, beam or column for its free body. For several together (a stack, a level, the whole structure), " +
                                   "Ctrl or Shift + click each first: they turn orange.", true));
            if (fbPicked.Count == 0) return;
            side.Children.Add(Para($"Picked: {string.Join(", ", fbPicked.OrderBy(x => x))}.", true));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var open = new Button { Content = $"Free body of them ({fbPicked.Count})", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
            open.Click += (s, e) => OpenFreeBody(fbPicked.ToList());
            var clear = new Button { Content = "Clear", Padding = new Thickness(8, 2, 8, 2) };
            clear.Click += (s, e) => { fbPicked.Clear(); view3D.HighlightIds = null; RefreshSide(); view3D.Show(B); };
            row.Children.Add(open);
            row.Children.Add(clear);
            side.Children.Add(row);
        }

        private StackPanel fbTable;

        private void FreeBodySide()
        {
            side.Children.Add(Head($"Free body: {FreeBodyName}"));
            var back = new Button { Content = "◀ Back to the foundation", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
            back.Click += (s, e) => CloseFreeBody();
            side.Children.Add(back);
            if (fb.Members.Count == 1)
                side.Children.Add(Para($"{(fb.Kind == "column" ? "" : $"{fb.Length:0.##} ft long, ")}from {fb.Bottom:0.##} to {fb.Top:0.##} ft. Click another wall, beam or column to draw its free body.", true));
            else
                side.Children.Add(Para($"{fb.Members.Count} members ({string.Join(", ", fbIds)}), from {fb.Bottom:0.##} to {fb.Top:0.##} ft. Ctrl + click one to take it out; " +
                                       "back to the foundation to pick others.", true));
            var inside = new CheckBox
            {
                Content = "Internal forces inside the body (both halves of each pair)", IsChecked = fbShowInside, Margin = new Thickness(0, 0, 0, 6),
                ToolTip = "On: the connections between two selected members are drawn and listed, both halves (they cancel), to check everything is accounted for. " +
                          "Off: the cleaner drawing, for reports.",
            };
            inside.Checked += (s, e) => { fbShowInside = true; Title3D(); RefreshFreeBodyTable(); RefreshFreeBodyWind(); view3D.Show(B); };
            inside.Unchecked += (s, e) => { fbShowInside = false; Title3D(); RefreshFreeBodyTable(); RefreshFreeBodyWind(); view3D.Show(B); };
            side.Children.Add(inside);
            if (AllWalls)
            {
                var framed = new CheckBox { Content = "Draw its framing (studs, plates, end posts)", IsChecked = fbFraming, Margin = new Thickness(0, 0, 0, 6),
                                            ToolTip = "Each story's studs at the model's size and spacing; off: a see-through block" };
                framed.Checked += (s, e) => { fbFraming = true; view3D.DrawOnly = false; view3D.Show(B); };
                framed.Unchecked += (s, e) => { fbFraming = false; view3D.DrawOnly = true; view3D.Show(B); };
                side.Children.Add(framed);
            }

            side.Children.Add(Head("Combination"));
            if (fbCombos.Count == 0)
            {
                comboList = null;
                side.Children.Add(Para("No gravity combination has the loads on it: drawn with the service loads.", true));
            }
            else
            {
                comboList = new ListBox { MaxHeight = 220, Margin = new Thickness(0, 0, 0, 6), BorderBrush = Ui.Line };
                ScrollViewer.SetHorizontalScrollBarVisibility(comboList, ScrollBarVisibility.Disabled);
                foreach (var c in fbCombos)
                    comboList.Items.Add(new TextBlock { Text = $"{c.Name}: {fb.HeldUp(c):N0} lb held up", TextWrapping = TextWrapping.Wrap });
                comboList.SelectedIndex = Math.Max(0, fbCombos.IndexOf(fbCombo));
                comboList.SelectionChanged += (s, e) =>
                {
                    if (settingList || comboList.SelectedIndex < 0) return;
                    ShowFreeBodyCombo(fbCombos[comboList.SelectedIndex]);
                };
                side.Children.Add(comboList);
                side.Children.Add(Para($"The gravity combinations ({MethodName}) with the loads on it, the most held up first. The roof's wind is not in these (Wind uplift).", true));
            }

            fbTable = new StackPanel();
            side.Children.Add(fbTable);
            RefreshFreeBodyTable();
            FreeBodyWindSide();
            foreach (var n in fb.Notes) side.Children.Add(Para("• " + n, true));
        }

        private string Where(FreeBodyForce f)
        {
            var m = fb.MemberOf(f.Member);
            string at = m?.Kind == "column" ? "" : f.IsLine ? $"{f.A:0.##}–{f.B:0.##}" : $"at {f.A:0.##}";
            return fb.Members.Count > 1 ? $"{f.Member} {at}".Trim() : at;
        }

        private void RefreshFreeBodyTable()
        {
            if (fbTable == null || fb == null) return;
            fbTable.Children.Clear();
            var c = fbCombo;
            void Rows(string letter, string title, Brush ink, List<FreeBodyForce> list, string none)
            {
                fbTable.Children.Add(new TextBlock { Text = $"{letter}  {title}", FontWeight = FontWeights.SemiBold, Foreground = ink, Margin = new Thickness(0, 6, 0, 2) });
                if (list.Count == 0) { fbTable.Children.Add(Para(none, true)); return; }
                var rows = new List<string[]> { new[] { "", "Along (ft)", "Elev.", "Load", "Total (lb)" } };
                rows.AddRange(list.Select(f => new[]
                {
                    f.What, Where(f), $"{f.Z:0.##}", f.IsLine ? $"{f.Value(c):N0} plf {(f.Up ? "↑" : "↓")}" : $"{f.Value(c):N0} lb {(f.Up ? "↑" : "↓")}", $"{f.Resultant(c):N0}",
                }));
                rows.Add(new[] { "Sum", "", "", "", $"{list.Sum(f => f.Up ? -f.Resultant(c) : f.Resultant(c)):N0} ↓" });
                fbTable.Children.Add(Mini(rows, new double[] { 110, 0, 0, 0, 0 }));
            }
            Rows("W", "Weight (down)", new SolidColorBrush(WeightColor), fb.Of(FreeBodyPart.Weight).ToList(), "None.");
            Rows("I", "Internal forces at the cut: the reactions of the members connected to it, not selected", new SolidColorBrush(CutColor),
                 fb.Of(FreeBodyPart.Internal).ToList(), "None: nothing connected to it is left out (it is cut only from the foundation).");
            Rows("S", "Support reactions: the foundation (up)", new SolidColorBrush(ReactionColor), fb.Of(FreeBodyPart.Support).ToList(), "None: it is not on the foundation.");
            Rows("E", "External applied loads (down)", new SolidColorBrush(DownColor), fb.Of(FreeBodyPart.External).ToList(), "None.");
            if (fbShowInside && fb.InsideForces.Any())
                Rows("", "Inside the body: both halves of each pair between two selected members (they cancel, not in the sums)", new SolidColorBrush(InsideColor),
                     fb.InsideForces.ToList(), "");
            double down = fb.Forces.Where(f => !f.Inside && !f.Up).Sum(f => f.Resultant(c)), up = fb.HeldUp(c), off = fb.Imbalance(c);
            fbTable.Children.Add(Para($"ΣFz: down {down:N0} − up {up:N0} = {off:N0} lb " + (Math.Abs(off) <= Math.Max(1, 1e-6 * up) ? "(in equilibrium)." : "— not in equilibrium: see the notes."), bold: true));
        }

        /// <summary>The wind case the free body's wind forces are listed in.</summary>
        private string fbWindCase;
        private StackPanel fbWindTable;

        /// <summary>
        /// The wind on the free body and what holds it against the wind, in one wind case at a time, by WISE (the lateral
        /// design's forces, W at strength level): E the wind's shear on its walls' tops, I the connections where it is cut, S
        /// the foundation's; ΣF and ΣM about all three axes.
        /// </summary>
        private void FreeBodyWindSide()
        {
            var cases = fb.WindCases;
            side.Children.Add(Head("Wind"));
            fbWindTable = null;
            if (cases.Count == 0)
            {
                side.Children.Add(Para(lateral == null
                    ? "No shear wall design saved (the shear wall app's Lateral section): no wind forces on it yet."
                    : "No wind forces on it in the shear wall design (no shear wall in it, and no hold-down lands on it).", true));
                return;
            }
            if (!cases.Contains(fbWindCase)) fbWindCase = cases[0];
            var pick = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var c in cases) pick.Items.Add(c);
            pick.SelectedItem = fbWindCase;
            pick.SelectionChanged += (s, e) => { fbWindCase = pick.SelectedItem as string; RefreshFreeBodyWind(); };
            side.Children.Add(pick);
            side.Children.Add(Para("The shear wall design's forces in this wind case, W at strength level, no dead load (the gravity is above). " +
                                   "Each connection is half of a pair: the member at its other end has the same force the other way.", true));
            fbWindTable = new StackPanel();
            side.Children.Add(fbWindTable);
            RefreshFreeBodyWind();
        }

        private void RefreshFreeBodyWind()
        {
            if (fbWindTable == null || fb == null) return;
            fbWindTable.Children.Clear();
            string c = fbWindCase;
            string Of(string member, double along) => fb.Members.Count > 1 ? $"{member} at {along:0.##}" : fb.Kind == "column" ? "" : $"at {along:0.##}";
            void Rows(string letter, string title, Brush ink, IEnumerable<FreeBodyShear> shears, IEnumerable<FreeBodyConnection> conns, string none)
            {
                var rows = new List<string[]> { new[] { "", "Along (ft)", "Elev.", "Force (lb)" } };
                foreach (var s in shears)
                    rows.Add(new[] { s.What, fb.Members.Count > 1 ? s.Member : "", $"{s.Z:0.##}", $"{s.Of(c):+#,0;-#,0;0} →" });
                foreach (var k in conns)
                    rows.Add(new[] { k.What, Of(k.Member, k.AlongIn(c)), $"{k.Z:0.##}", $"{k.On(c):+#,0;-#,0;0} ↑" });
                fbWindTable.Children.Add(new TextBlock { Text = $"{letter}  {title}", FontWeight = FontWeights.SemiBold, Foreground = ink, Margin = new Thickness(0, 6, 0, 2) });
                if (rows.Count == 1) { fbWindTable.Children.Add(Para(none, true)); return; }
                fbWindTable.Children.Add(Mini(rows, new double[] { 110, 0, 0, 0 }));
            }
            var cut = fb.Connections.Where(k => !k.Inside).ToList();
            Rows("E", "The wind on it", new SolidColorBrush(DownColor), fb.Shears.Where(s => s.Part == FreeBodyPart.External), Enumerable.Empty<FreeBodyConnection>(), "None.");
            Rows("I", "At the cut: the connections to members not selected, the sills on a floor", new SolidColorBrush(CutColor),
                 fb.Shears.Where(s => s.Part == FreeBodyPart.Internal), cut.Where(k => k.Part == FreeBodyPart.Internal), "None.");
            Rows("S", "The foundation: hold-downs, compression posts, sills", new SolidColorBrush(ReactionColor),
                 fb.Shears.Where(s => s.Part == FreeBodyPart.Support), cut.Where(k => k.Part == FreeBodyPart.Support), "None: it is not on the foundation.");
            if (fbShowInside && fb.Connections.Any(k => k.Inside))
                Rows("", "Inside the body: both halves of each pair (they cancel, not in the sums)", new SolidColorBrush(InsideColor),
                     Enumerable.Empty<FreeBodyShear>(), fb.Connections.Where(k => k.Inside), "");
            var f = fb.WindForce(c);
            var m = fb.WindMoment(c);
            string off = fb.WindOutOfBalance(c);
            fbWindTable.Children.Add(Para($"ΣF = ({f.x:N0}, {f.y:N0}, {f.z:N0}) lb, ΣM = ({m.x:N0}, {m.y:N0}, {m.z:N0}) lb·ft about its middle at its base " +
                                          $"(x east, y north, z up; of {fb.WindMomentScale(c):N0})" +
                                          (off == null ? ": in equilibrium." : $" — not in equilibrium ({off}): a force is not accounted for."), bold: true));
        }

        // ------------------------------------------------------------------ the free body in 3D

        /// <summary>A member as drawn: its top and bottom (ft) and a side of it on plan.</summary>
        private (double top, double bottom, Point2 nrm) Frame(FreeBodyMember m)
        {
            Point2 nrm = new Point2(1, 0);
            if (m.Length > 0) nrm = new Point2(-(m.End.Y - m.Start.Y) / m.Length, (m.End.X - m.Start.X) / m.Length);
            if (m.Kind == "wall") return (m.Top - Building3DView.SlabThickness, m.Bottom, nrm);
            if (m.Kind == "beam") { double t = m.Top - Building3DView.SlabThickness; return (t, t - Building3DView.BeamDepth, nrm); }
            var col = B.Find(m.Id) as BuildingModel.Model.Column;
            return (col != null ? Building3DView.ColumnTop(B, col) : m.Top, m.Bottom, new Point2(1, 0));
        }

        /// <summary>
        /// The free body in 3D, its members drawn alone (by the view): the loads applied to it in blue, arrows onto the top (or
        /// onto a face at a level it passes); its own weight in green, down through its middle; the foundation's reactions in
        /// red and the cut's in purple, up under it or down onto it as they act; with the internal forces shown, the pairs
        /// inside it in grey. Line loads are rows of arrows, as tall as the load; point loads one arrow, as long as it.
        /// </summary>
        private Model3D FreeBody3D()
        {
            if (fb == null) return null;
            var g = new Model3DGroup();
            var c = fbCombo;
            Material Solid(Color k) => Mat(k);
            if (AllWalls && fbFraming) foreach (var m in fb.Members) WallFraming3D(g, m);

            var shown = fb.Forces.Where(f => !f.Inside || fbShowInside).ToList();
            // the scales: the largest line load 3 ft tall, the largest point load 5 ft long (at least a quarter of the body's height)
            double height = Math.Max(4, fb.Top - fb.Bottom);
            double maxL = Math.Max(1, shown.Where(f => f.IsLine).Select(f => Math.Abs(f.Value(c))).DefaultIfEmpty(0).Max());
            double maxP = Math.Max(1, shown.Where(f => !f.IsLine).Select(f => Math.Abs(f.Value(c))).DefaultIfEmpty(0).Max());
            double kL = Math.Max(2.5, 0.3 * height) / maxL, kP = Math.Max(4, 0.45 * height) / maxP;

            foreach (var f in shown)
            {
                double v = f.Value(c);
                if (Math.Abs(v) < 1e-6) continue;
                var m = fb.MemberOf(f.Member);
                var (top, bottom, nrm) = Frame(m);
                Point2 At(double along, double side) => new Point2(m.At(along).X + nrm.X * side, m.At(along).Y + nrm.Y * side);
                var ink = Solid(f.Inside ? InsideColor : f.Part == FreeBodyPart.External ? DownColor : f.Part == FreeBodyPart.Support ? ReactionColor
                              : f.Part == FreeBodyPart.Internal ? CutColor : WeightColor);
                if (f.Part == FreeBodyPart.Weight)
                {
                    // through its middle, in its own plane
                    double mid = (top + bottom) / 2;
                    double len = f.IsLine ? v * kL : v * kP;
                    if (f.IsLine) LineArrows(g, At(f.A, 0), At(f.B, 0), mid + len / 2, mid - len / 2, ink);
                    else Arrow(g, At(f.A, 0), mid + len / 2, mid - len / 2, ink, 0.12, 0.4);
                    continue;
                }
                // a pair's two halves inside the body side by side, so both show
                double side = f.Inside ? (f.Up ? -0.25 : 0.25) : 0;
                if (f.Up) Draw(f, bottom, false);
                else
                {
                    // onto the top where it lands at the member's top; at a level it passes, onto the top of the story below
                    bool atTop = m.Kind == "beam" || Math.Abs(f.Z - m.Top) < 1e-6;
                    Draw(f, atTop ? top : f.Z - Building3DView.SlabThickness, true);
                }

                void Draw(FreeBodyForce x, double tip, bool down)
                {
                    if (x.IsLine)
                    {
                        double h = v * kL;
                        var a = At(x.A, side); var b = At(x.B, side);
                        if (down) LineArrows(g, a, b, tip + h, tip, ink);
                        else LineArrows(g, a, b, tip - h, tip, ink);
                    }
                    else
                    {
                        double len = Math.Max(1.0, v * kP);
                        var p = At(x.A, side);
                        if (down) Arrow(g, p, tip + len, tip, ink, 0.14, 0.45);
                        else Arrow(g, p, tip - len, tip, ink, 0.14, 0.45);
                    }
                }
            }
            return g;
        }

        /// <summary>The walls drawn as framed (studs, plates, posts) rather than see-through blocks.</summary>
        private bool fbFraming = true;

        /// <summary>
        /// A wall's framing, story by story, from the shared layout (WoodFraming): each story's studs at the model's size and
        /// spacing (the wall's own, else the project's), on the floor at its base up to the underside of the floor above.
        /// </summary>
        private void WallFraming3D(Model3DGroup g, FreeBodyMember m)
        {
            var w = B.Find(m.Id) as BuildingModel.Model.Wall;
            if (w == null) return;
            double baseZ = m.Bottom;
            foreach (var lv in BuildingModel.Model.BuildingQueries.LevelsAlong(B, w))
            {
                double h = lv.Elevation - baseZ - Building3DView.SlabThickness;
                if (h > 1)
                {
                    var f = WoodFraming.WallFraming.Build(m.Length, h, B.StudSizeOf(w, lv.Id), B.StudSpacingOf(w, lv.Id), 0);
                    var model = WoodFraming.Wpf.FramingModel.Wall(f);
                    model.Transform = WoodFraming.Wpf.FramingModel.OnPlan(w.Start.X, w.Start.Y, w.End.X, w.End.Y, baseZ, f.StudDepth);
                    g.Children.Add(model);
                }
                baseZ = lv.Elevation;
            }
        }
    }
}
