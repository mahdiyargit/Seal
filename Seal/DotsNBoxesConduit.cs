namespace Seal
{
    using Rhino.Display;
    using Rhino.Geometry;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;

    namespace Seal
    {
        internal class DotsNBoxesConduit : DisplayConduit
        {
            public Mesh Mesh { get; set; }
            public Color UserCol { get; set; }
            public Color SealCol { get; set; }
            public List<int> UserLines { get; set; }
            public List<int> SeaLines { get; set; }
            public List<int> UserFaces { get; set; }
            public List<int> SealFaces { get; set; }
            public Line DrawingLine { get; set; }
            protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
            {
                base.CalculateBoundingBox(e);
                e.IncludeBoundingBox(Mesh.GetBoundingBox(Plane.WorldXY));
            }
            protected override void PreDrawObject(DrawObjectEventArgs e)
            {
                base.PreDrawObject(e);
                PostDrawObjects(e);
                e.Display.DrawMeshShaded(Mesh, new DisplayMaterial(UserCol), UserFaces.ToArray());
                e.Display.DrawMeshShaded(Mesh, new DisplayMaterial(SealCol), SealFaces.ToArray());
                e.Display.DrawLines(UserLines.Select(i => Mesh.TopologyEdges.EdgeLine(i)), UserCol, 4);
                e.Display.DrawLines(SeaLines.Select(i => Mesh.TopologyEdges.EdgeLine(i)), SealCol, 4);
            }
            protected override void DrawOverlay(DrawEventArgs e)
            {
                base.DrawOverlay(e);
                var userScore = UserFaces.Count == 0 ? 0.0 : (double)UserFaces.Count / Mesh.Faces.Count;
                var sealScore = SealFaces.Count == 0 ? 0.0 : (double)SealFaces.Count / Mesh.Faces.Count;
                e.Display.DrawLine(DrawingLine, SealCol, 6);
                e.Display.Draw2dRectangle(new Rectangle(8, 55, 200, 22), UserCol, 0, Color.FromArgb(80, UserCol));
                e.Display.Draw2dRectangle(new Rectangle(8, 80, 200, 22), SealCol, 0, Color.FromArgb(80, SealCol));
                e.Display.Draw2dRectangle(new Rectangle(8, 55, (int)(userScore * 200), 22), UserCol, 0, Color.FromArgb(120, UserCol));
                e.Display.Draw2dRectangle(new Rectangle(8, 80, (int)(sealScore * 200), 22), SealCol, 0, Color.FromArgb(120, SealCol));
                e.Display.Draw2dText($@"Your Score: {UserFaces.Count}", Color.White, new Point2d(10, 60), false, 20, "Lucida Console");
                e.Display.Draw2dText($@"Seal Score: {SealFaces.Count}", Color.White, new Point2d(10, 85), false, 20, "Lucida Console");
            }
        }
    }
}
