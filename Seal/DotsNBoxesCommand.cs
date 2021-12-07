using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using Seal.Seal;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Seal
{
    public class DotsNBoxesCommand : Command
    {
        private DotsNBoxesConduit _conduit;
        private int _playground, _start, _x = 5, _y = 5;
        private OptionColor _userColor = new OptionColor(Color.Blue);
        private OptionColor _sealColor = new OptionColor(Color.Red);
        private Mesh _m;
        private List<int> _freeEdges;
        private int[] _facesValue;
        public DotsNBoxesCommand() => Instance = this;
        public static DotsNBoxesCommand Instance { get; private set; }
        public override string EnglishName => "sealDotsNBoxes";
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            using (var go = new GetOption())
            {
                go.SetCommandPrompt("Choose Options");
                var pOptionIndex = go.AddOptionList("Playground", new[] { "QaudMeshPlane", "TriMeshPlane", "CustomMesh" }, _playground);
                var sOptionIndex = go.AddOptionList("WhoStarts", new[] { "Random", "Player", "TheSeal" }, _start);
                go.AddOptionColor("PlayerColor", ref _userColor);
                go.AddOptionColor("SealColor", ref _sealColor);
                while (true)
                {
                    if (go.CommandResult() != Result.Success)
                        return go.CommandResult();
                    if (go.Get() == GetResult.Option)
                    {
                        if (go.OptionIndex() == pOptionIndex)
                            _playground = go.Option().CurrentListOptionIndex;
                        else if (go.OptionIndex() == sOptionIndex)
                            _start = go.Option().CurrentListOptionIndex;
                        continue;
                    }
                    break;
                }
            }
            _m = null;
            var guid = Guid.Empty;
            if (_playground == 2) //Custom Mesh
            {
                var gm = new GetObject { GeometryFilter = ObjectType.Mesh };
                gm.SetCommandPrompt("Select a Mesh");
                gm.DisablePreSelect();
                gm.Get();
                if (gm.CommandResult() != Result.Success)
                {
                    RhinoApp.WriteLine("No Mesh was selected");
                    return gm.CommandResult();
                }
                _m = gm.Object(0)?.Mesh();
                if (_m is null)
                {
                    RhinoApp.WriteLine("Invalid mesh");
                    return gm.CommandResult();
                }
                gm.Object(0).Object().Highlight(false);
                _m.Weld(doc.ModelAngleToleranceRadians);
            }
            else
            {
                var result = RhinoGet.GetRectangleWithCounts(2, ref _x, 2, ref _y, out var corners);
                if (result != Result.Success) return result;
                var rec = Rectangle3d.CreateFromPolyline(corners);
                _m = _playground == 0 ?
                    Mesh.CreateFromPlane(rec.Plane, rec.X, rec.Y, _x, _y) :
                    CreateTriMeshPlane(rec, _x, _y);
                guid = doc.Objects.AddMesh(_m);
            }
            var rnd = new Random();
            bool userTurn;
            if (_start == 0)
                userTurn = rnd.NextDouble() > 0.5;
            else
                userTurn = _start == 1;
            _conduit = new DotsNBoxesConduit()
            {
                Enabled = true,
                Mesh = _m,
                UserCol = _userColor.CurrentValue,
                SealCol = _sealColor.CurrentValue,
                UserLines = new List<int>(),
                SeaLines = new List<int>(),
                UserFaces = new List<int>(),
                SealFaces = new List<int>(),
                DrawingLine = Line.Unset,
            };
            _freeEdges = Enumerable.Range(0, _m.TopologyEdges.Count).ToList();
            _facesValue = _m.Faces.Select(mf => mf.IsQuad ? 4 : 3).ToArray();
            while (_freeEdges.Count != 0)
            {
                if (userTurn)
                {
                    var gp = new GetPoint();
                    gp.SetCommandPrompt("Your Turn");
                    gp.Constrain(_m, false);
                    gp.PermitObjectSnap(false);
                    gp.DynamicDraw += OnDynamicDraw;
                    if (gp.Get() != GetResult.Point)
                    {
                        RhinoApp.WriteLine("No edge was selected.");
                        _conduit.Enabled = false;
                        _conduit = null;
                        if (guid != Guid.Empty)
                            doc.Objects.Delete(guid, true);
                        doc.Views.Redraw();
                        return gp.CommandResult();
                    }
                    var pt = gp.Point();
                    if (!ClosestEdgeIndex(pt, out var index)) continue;
                    _conduit.UserLines.Add(index);
                    doc.Views.Redraw();
                    _freeEdges.Remove(index);
                    userTurn = false;
                    foreach (var i in _m.TopologyEdges.GetConnectedFaces(index))
                    {
                        _facesValue[i]--;
                        if (_facesValue[i] > 0) continue;
                        _conduit.UserFaces.Add(i);
                        doc.Views.Redraw();
                        userTurn = true;
                    }
                }
                else
                {
                    var index = 0;
                    var edgeScores = _freeEdges.Select(EdgeScore).ToArray();
                    for (var i = 2; i >= -2; i--)
                    {
                        var indices = edgeScores.Select((score, j) => score == i ? j : -1).Where(j => j != -1)
                            .ToArray();
                        if (indices.Length <= 0) continue;
                        index = _freeEdges[indices[rnd.Next(indices.Length)]];
                        break;
                    }
                    DrawLine(_m.TopologyEdges.EdgeLine(index), doc);
                    _conduit.SeaLines.Add(index);
                    doc.Views.Redraw();
                    _freeEdges.Remove(index);
                    userTurn = true;
                    foreach (var i in _m.TopologyEdges.GetConnectedFaces(index))
                    {
                        _facesValue[i]--;
                        if (_facesValue[i] > 0) continue;
                        _conduit.SealFaces.Add(i);
                        doc.Views.Redraw();
                        userTurn = false;
                    }
                }
            }
            var userScore = _conduit.UserFaces.Count;
            var sealScore = _conduit.SealFaces.Count;
            string prompt;
            if (userScore == sealScore)
                prompt = "No One Wins! Press Enter To Exit";
            else if (userScore < sealScore)
                prompt = "The Seal Wins! Press Enter To Exit";
            else
                prompt = "Congratulation! You Win! Press Enter To Exit";
            var _ = string.Empty;
            RhinoGet.GetString(prompt, false, ref _);
            _conduit.Enabled = false;
            _conduit = null;
            if (guid != Guid.Empty)
                doc.Objects.Delete(guid, true);
            doc.Views.Redraw();
            return Result.Success;
        }
        private static Mesh CreateTriMeshPlane(Rectangle3d r, int u, int v)
        {
            var uStep = 1.0 / u;
            var vStep = 1.0 / v;
            var meshes = new List<Mesh>();
            for (var i = 0; i <= u - 1; i++)
            {
                for (var j = 0; j <= v; j++)
                {
                    var m = new Mesh();
                    m.Faces.AddFace(0, 1, 2);
                    if ((i + j) % 2 == 0)
                        m.Vertices.AddVertices(new List<Point3d>
                        {
                            r.PointAt(i * uStep, j * vStep),
                            j <= 0 ? r.PointAt((i + 1) * uStep, j * vStep) : r.PointAt((i + 1) * uStep, (j - 1) * vStep),
                            j >= v ? r.PointAt((i + 1) * uStep, j * vStep) : r.PointAt((i + 1) * uStep, (j + 1) * vStep)
                        });
                    else
                        m.Vertices.AddVertices(new List<Point3d>
                        {
                            r.PointAt((i + 1) * uStep, j * vStep),
                            j >= v ? r.PointAt(i * uStep, j * vStep) : r.PointAt(i * uStep, (j + 1) * vStep),
                            j <= 0 ? r.PointAt(i * uStep, j * vStep) : r.PointAt(i * uStep, (j - 1) * vStep)
                        });
                    meshes.Add(m);
                }
            }
            var mesh = new Mesh();
            mesh.Append(meshes);
            mesh.Weld(0.0);
            mesh.RebuildNormals();
            return mesh;
        }
        private void OnDynamicDraw(object sender, GetPointDrawEventArgs e)
        {
            if (!ClosestEdgeIndex(e.CurrentPoint, out var index)) return;
            e.Display.DrawLine(_m.TopologyEdges.EdgeLine(index), _userColor.CurrentValue, 6);
        }
        private bool ClosestEdgeIndex(Point3d pt, out int index)
        {
            var indices = _m.TopologyEdges.GetEdgesForFace(_m.ClosestMeshPoint(pt, 0.0).FaceIndex);
            var distances = indices.Select(i => _m.TopologyEdges.EdgeLine(i).DistanceTo(pt, true)).ToArray();
            index = indices[Array.IndexOf(distances, distances.Min())];
            return _freeEdges.Contains(index);
        }
        private void DrawLine(Line line, RhinoDoc doc)
        {
            double parameter;
            var utcNow = DateTime.UtcNow;
            do
            {
                parameter = Math.Min(1.0, (DateTime.UtcNow - utcNow).Ticks * 2E-07);
                _conduit.DrawingLine = new Line(line.From, line.PointAt(parameter));
                doc.Views.Redraw();
            } while (parameter < 1.0);
            _conduit.DrawingLine = Line.Unset;
        }
        private int EdgeScore(int index)
        {
            var score = 0;
            foreach (var i in _m.TopologyEdges.GetConnectedFaces(index))
            {
                var fv = _facesValue[i];
                switch (fv)
                {
                    case 1 when score == -1:
                        score = 2;
                        break;
                    case 1:
                        score++;
                        break;
                    case 2 when score == 1:
                        score = 2;
                        break;
                    case 2:
                        score--;
                        break;
                }
            }
            return score;
        }
    }
}
