using System;
using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using FluxCad48.Geometry;
using FluxCad48.ShapeViewAnalysis;

namespace FluxCad48.Commands
{
	public class FluxDebugViewIslandsCommand
	{
		[CommandMethod("FLUX_DEBUG_VIEW_ISLANDS")]
		public void Run()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Editor ed = doc.Editor;
			Database db = doc.Database;

			PromptSelectionOptions pso = new PromptSelectionOptions();
			pso.MessageForAdding = "\n형상 뷰 영역을 선택하세요: ";

			PromptSelectionResult psr = ed.GetSelection(pso);

			if (psr.Status != PromptStatus.OK)
			{
				AppendLog(ed, "[ViewIsland] 선택이 취소되었습니다.");
				return;
			}

			List<SheetEntity> entities = new List<SheetEntity>();

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				foreach (SelectedObject so in psr.Value)
				{
					if (so == null || so.ObjectId.IsNull)
						continue;

					Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
					if (ent == null)
						continue;

					SheetEntity se = ConvertToSheetEntity(ent);
					if (se != null)
						entities.Add(se);
				}

				tr.Commit();
			}

			AppendLog(ed, "");
			AppendLog(ed, "[ViewIsland] Selected=" + entities.Count);

			SelectedShapeViewSet set = SelectedShapeViewClassifier.Classify(entities);

			AppendLog(ed, string.Format(
				"[ViewIsland] Geometry={0}, Dimension={1}, Text={2}, Unknown={3}",
				set.GeometryEntities.Count,
				set.DimensionEntities.Count,
				set.TextEntities.Count,
				set.UnknownEntities.Count));

			ViewIslandBuildOptions options = new ViewIslandBuildOptions();

			List<ViewIsland> islands = ViewIslandBuilder.Build(
				set.GeometryEntities,
				options);

			AppendLog(ed, "[ViewIsland] IslandCount=" + islands.Count);

			for (int i = 0; i < islands.Count; i++)
			{
				ViewIsland island = islands[i];

				AppendLog(ed, string.Format(
					"[Island {0}] Geom={1}, Bounds={2}, Width={3:0.###}, Height={4:0.###}, ThinRatio={5:0.###}, ThinCandidate={6}",
					island.Index,
					island.GeometryEntities.Count,
					island.Bounds,
					island.Width,
					island.Height,
					island.ThinnessRatio,
					island.IsThinViewCandidate));
			}
		}

		private static SheetEntity ConvertToSheetEntity(Entity ent)
		{
			SheetEntity se = new SheetEntity();

			se.Handle = ent.Handle.ToString();
			se.EntityType = ent.GetType().Name;
			se.Layer = ent.Layer;
			se.Kind = GetKind(ent);
			se.Bounds = GetBounds(ent);

			if (se.Bounds != null && se.Bounds.IsValid)
			{
				se.Anchor = new Point2D(
					se.Bounds.CenterX,
					se.Bounds.CenterY);
			}

			FillGeometryInfo(ent, se);
			FillTextInfo(ent, se);

			return se;
		}

		private static SheetEntityKind GetKind(Entity ent)
		{
			if (ent is Dimension)
				return SheetEntityKind.Dimension;

			if (ent is DBText)
				return SheetEntityKind.Text;

			if (ent is MText)
				return SheetEntityKind.MText;

			if (ent is AttributeReference)
				return SheetEntityKind.InsertAttribute;

			if (ent is Line)
				return SheetEntityKind.Line;

			if (ent is Polyline)
				return SheetEntityKind.Polyline;

			if (ent is Arc)
				return SheetEntityKind.Arc;

			if (ent is Circle)
				return SheetEntityKind.Circle;

			if (ent is Ellipse)
				return SheetEntityKind.Ellipse;

			if (ent is Spline)
				return SheetEntityKind.Spline;

			if (ent is Hatch)
				return SheetEntityKind.Hatch;

			if (ent is BlockReference)
				return SheetEntityKind.BlockReference;

			return SheetEntityKind.Unknown;
		}

		private static Bounds2D GetBounds(Entity ent)
		{
			try
			{
				Extents3d ext = ent.GeometricExtents;

				return new Bounds2D(
					ext.MinPoint.X,
					ext.MinPoint.Y,
					ext.MaxPoint.X,
					ext.MaxPoint.Y);
			}
			catch
			{
				return null;
			}
		}

		private static void FillGeometryInfo(Entity ent, SheetEntity se)
		{
			Line line = ent as Line;
			if (line != null)
			{
				se.StartPoint = ToPoint2D(line.StartPoint);
				se.EndPoint = ToPoint2D(line.EndPoint);
				return;
			}

			Polyline pl = ent as Polyline;
			if (pl != null)
			{
				se.IsClosed = pl.Closed;

				for (int i = 0; i < pl.NumberOfVertices; i++)
				{
					Point2d p = pl.GetPoint2dAt(i);
					se.Vertices.Add(new Point2D(p.X, p.Y));
				}

				if (pl.NumberOfVertices >= 1)
					se.StartPoint = se.Vertices[0];

				if (pl.NumberOfVertices >= 2)
					se.EndPoint = se.Vertices[pl.NumberOfVertices - 1];

				return;
			}

			Arc arc = ent as Arc;
			if (arc != null)
			{
				se.CenterPoint = ToPoint2D(arc.Center);
				se.Radius = arc.Radius;
				se.StartPoint = ToPoint2D(arc.StartPoint);
				se.EndPoint = ToPoint2D(arc.EndPoint);
				se.StartAngleDeg2D = arc.StartAngle * 180.0 / Math.PI;
				se.EndAngleDeg2D = arc.EndAngle * 180.0 / Math.PI;
				return;
			}

			Circle circle = ent as Circle;
			if (circle != null)
			{
				se.CenterPoint = ToPoint2D(circle.Center);
				se.Radius = circle.Radius;
				se.IsClosed = true;
				return;
			}

			Ellipse ellipse = ent as Ellipse;
			if (ellipse != null)
			{
				se.CenterPoint = ToPoint2D(ellipse.Center);
				se.MajorRadius = ellipse.MajorRadius;
				se.MinorRadius = ellipse.MinorRadius;
				se.IsClosed = ellipse.Closed;
				return;
			}
		}

		private static void FillTextInfo(Entity ent, SheetEntity se)
		{
			DBText dbText = ent as DBText;
			if (dbText != null)
			{
				se.Text = dbText.TextString ?? "";
				se.TextNormalized = NormalizeText(se.Text);
				se.TextHeight = dbText.Height;
				se.RotationDeg = dbText.Rotation * 180.0 / Math.PI;
				return;
			}

			MText mt = ent as MText;
			if (mt != null)
			{
				se.Text = mt.Contents ?? "";
				se.TextNormalized = NormalizeText(se.Text);
				se.TextHeight = mt.TextHeight;
				se.RotationDeg = mt.Rotation * 180.0 / Math.PI;
				return;
			}

			AttributeReference ar = ent as AttributeReference;
			if (ar != null)
			{
				se.Text = ar.TextString ?? "";
				se.TextNormalized = NormalizeText(se.Text);
				se.TextHeight = ar.Height;
				se.RotationDeg = ar.Rotation * 180.0 / Math.PI;
				return;
			}
		}

		private static string NormalizeText(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return "";

			return text.Trim().ToUpperInvariant();
		}

		private static Point2D ToPoint2D(Point3d p)
		{
			return new Point2D(p.X, p.Y);
		}

		private static void AppendLog(Editor ed, string message)
		{
			ed.WriteMessage("\n" + message);
		}
	}
}
