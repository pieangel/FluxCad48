using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using FluxCad48.Brics;
using FluxCad48.Geometry;
using FluxCad48.ShapeViewAnalysis;
using FluxCad48.ShapeViewAnalysis.Loops;
using FluxCad48.Sheets;
using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace FluxCad48.Commands
{
	public class FluxDebugViewIslandsCommand
	{
		[CommandMethod("FLUX_DEBUG_CONTAINING_SHEET_FRAME")]
		public void FluxDebugContainingSheetFrame()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			PromptSelectionResult psr = ed.GetSelection();

			if (psr.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n선택된 객체가 없습니다.");
				return;
			}

			ObjectId[] ids = psr.Value.GetObjectIds();

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				List<Entity> selectedEntities = new List<Entity>();

				foreach (ObjectId id in ids)
				{
					Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
					if (ent == null)
						continue;

					selectedEntities.Add(ent);
				}

				Bounds2D selectedBounds =
					GetBoundsFromEntities(selectedEntities);

				if (selectedBounds == null || !selectedBounds.IsValid)
				{
					ed.WriteMessage("\n[ContainingFrameTest] 선택 객체 Bounds를 계산하지 못했습니다.");
					tr.Commit();
					return;
				}

				ed.WriteMessage(
					"\n[ContainingFrameTest] SelectedEntities=" +
					selectedEntities.Count +
					", SelectedBounds=" +
					selectedBounds);

				List<Entity> modelSpaceEntities =
					CollectModelSpaceEntities(db, tr);

				ed.WriteMessage(
					"\n[ContainingFrameTest] ModelSpaceEntities=" +
					modelSpaceEntities.Count);

				SheetFrameCandidate best =
					SheetFrameDetector.FindOwningFrameWithExpansion(
						selectedEntities,
						modelSpaceEntities,
						ed);

				if (best == null)
				{
					ed.WriteMessage("\n소속 SheetFrame 후보를 찾지 못했습니다.");
					tr.Commit();
					return;
				}

				ed.WriteMessage(
					"\n[ContainingFrameTest] BestFrame=" +
					best.Handle +
					", Type=" + best.EntityType +
					", Bounds=" + best.Bounds +
					", Score=" + best.Score);

				DrawDebugRectangle(
					tr,
					db,
					best.Bounds,
					"FLUX_MARKER",
					1);

				tr.Commit();
			}
		}

		private static List<Entity> CollectModelSpaceEntities(
			Database db,
			Transaction tr)
		{
			var result = new List<Entity>();

			BlockTable bt =
				(BlockTable)tr.GetObject(
					db.BlockTableId,
					OpenMode.ForRead);

			BlockTableRecord ms =
				(BlockTableRecord)tr.GetObject(
					bt[BlockTableRecord.ModelSpace],
					OpenMode.ForRead);

			foreach (ObjectId id in ms)
			{
				Entity ent =
					tr.GetObject(id, OpenMode.ForRead) as Entity;

				if (ent == null)
					continue;

				result.Add(ent);
			}

			return result;
		}

		private static void DrawDebugRectangle(
			Transaction tr,
			Database db,
			Bounds2D bounds,
			string layerName,
			short colorIndex)
		{
			if (bounds == null || !bounds.IsValid)
				return;

			BricscadEntityTools.EnsureLayer(tr, db, layerName);

			BlockTable bt =
				(BlockTable)tr.GetObject(
					db.BlockTableId,
					OpenMode.ForRead);

			BlockTableRecord modelSpace =
				(BlockTableRecord)tr.GetObject(
					bt[BlockTableRecord.ModelSpace],
					OpenMode.ForWrite);

			Polyline rect =
				BricscadEntityTools.CreateRectanglePolyline(bounds);

			rect.Layer = layerName;
			rect.Color =
				Teigha.Colors.Color.FromColorIndex(
					Teigha.Colors.ColorMethod.ByAci,
					colorIndex);

			rect.LineWeight = LineWeight.LineWeight050;

			modelSpace.AppendEntity(rect);
			tr.AddNewlyCreatedDBObject(rect, true);
		}

		private static Bounds2D GetBoundsFromEntities(
			IReadOnlyList<Entity> entities)
		{
			Bounds2D result = null;

			foreach (Entity ent in entities)
			{
				if (ent == null)
					continue;

				Bounds2D b = BricscadEntityTools.GetEntityBounds(ent);

				if (b == null || !b.IsValid)
					continue;

				if (result == null)
				{
					result = new Bounds2D(
						b.MinX,
						b.MinY,
						b.MaxX,
						b.MaxY);
				}
				else
				{
					result = new Bounds2D(
						System.Math.Min(result.MinX, b.MinX),
						System.Math.Min(result.MinY, b.MinY),
						System.Math.Max(result.MaxX, b.MaxX),
						System.Math.Max(result.MaxY, b.MaxY));
				}
			}

			return result;
		}


		[CommandMethod("FLUX_COPY_VIEW_ISLAND_LOOP_ENTITIES_BELOW")]
		public void CopyViewIslandLoopEntitiesBelow()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Editor ed = doc.Editor;
			Database db = doc.Database;

			PromptSelectionOptions pso = new PromptSelectionOptions();
			pso.MessageForAdding =
				"\nLoop 입력 Entity만 아래로 복사할 형상 뷰 영역을 선택하세요: ";

			PromptSelectionResult psr = ed.GetSelection(pso);

			if (psr.Status != PromptStatus.OK)
			{
				AppendLog(ed, "[LoopWorkspace] 선택이 취소되었습니다.");
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

					CollectSheetEntities(ent, tr, entities);
				}

				tr.Commit();
			}

			SelectedShapeViewSet set = SelectedShapeViewClassifier.Classify(entities);

			ViewIslandBuildOptions options = new ViewIslandBuildOptions();

			List<ViewIsland> islands = ViewIslandBuilder.Build(
				set.GeometryEntities,
				options);

			ViewIslandRoleClassifier.ClassifyAll(islands);

			CopyLoopEntitiesBelow(db, ed, islands);
		}

		private static void CopyLoopEntitiesBelow(
	Database db,
	Editor ed,
	List<ViewIsland> islands)
		{
			if (islands == null || islands.Count == 0)
			{
				AppendLog(ed, "[LoopWorkspace] ViewIsland가 없습니다.");
				return;
			}

			Bounds2D allBounds = GetIslandTotalBounds(islands);

			if (allBounds == null || !allBounds.IsValid)
			{
				AppendLog(ed, "[LoopWorkspace] 전체 Bounds 계산 실패.");
				return;
			}

			// 선택된 형상 영역 전체를 그대로 아래로 평행 이동한다.
			double gapY = Math.Max(allBounds.Height * 0.35, 300.0);

			double dx = 0.0;
			double dy = -(allBounds.Height + gapY);

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				BlockTableRecord space =
					tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;

				if (space == null)
					return;

				for (int i = 0; i < islands.Count; i++)
				{
					ViewIsland island = islands[i];

					List<SheetEntity> loopEntities =
						CollectLoopExtractableEntitiesForWorkspace(island);

					if (loopEntities.Count == 0)
						continue;

					AddIslandLabel(
						space,
						tr,
						island,
						island.Bounds.MinX + dx,
						island.Bounds.MaxY + dy + 30.0);

					for (int j = 0; j < loopEntities.Count; j++)
					{
						Entity copied = CreateEntityFromSheetEntity(loopEntities[j], dx, dy);

						if (copied == null)
							continue;

						copied.ColorIndex = 7;

						space.AppendEntity(copied);
						tr.AddNewlyCreatedDBObject(copied, true);
					}

					AppendLog(ed, string.Format(
						"[LoopWorkspace] Island={0}, LoopEntities={1}, dx={2:0.###}, dy={3:0.###}, CopiedBelow=OK",
						island.Index,
						loopEntities.Count,
						dx,
						dy));
				}

				tr.Commit();
			}
		}

		private static List<SheetEntity> CollectLoopExtractableEntitiesForWorkspace(
			ViewIsland island)
		{
			List<SheetEntity> result = new List<SheetEntity>();

			if (island == null || island.GeometryEntities == null)
				return result;

			for (int i = 0; i < island.GeometryEntities.Count; i++)
			{
				SheetEntity e = island.GeometryEntities[i];

				if (e == null)
					continue;

				if (!e.IsLoopExtractableGeometry)
					continue;

				result.Add(e);
			}

			return result;
		}

		private static Bounds2D GetIslandTotalBounds(List<ViewIsland> islands)
		{
			Bounds2D result = null;

			for (int i = 0; i < islands.Count; i++)
			{
				ViewIsland island = islands[i];

				if (island == null || island.Bounds == null || !island.Bounds.IsValid)
					continue;

				if (result == null)
				{
					result = new Bounds2D(
						island.Bounds.MinX,
						island.Bounds.MinY,
						island.Bounds.MaxX,
						island.Bounds.MaxY);
				}
				else
				{
					result.ExpandToInclude(island.Bounds);
				}
			}

			return result;
		}

		private static void AddIslandLabel(
			BlockTableRecord space,
			Transaction tr,
			ViewIsland island,
			double x,
			double y)
		{
			DBText text = new DBText();

			text.Position = new Point3d(x, y, 0.0);
			text.Height = 25.0;
			text.TextString = string.Format(
				"ViewIsland {0}  LoopEntities={1}",
				island.Index,
				CountLoopExtractableEntities(island));
			text.ColorIndex = 1;

			space.AppendEntity(text);
			tr.AddNewlyCreatedDBObject(text, true);
		}

		private static int CountLoopExtractableEntities(ViewIsland island)
		{
			if (island == null || island.GeometryEntities == null)
				return 0;

			int count = 0;

			for (int i = 0; i < island.GeometryEntities.Count; i++)
			{
				SheetEntity e = island.GeometryEntities[i];

				if (e != null && e.IsLoopExtractableGeometry)
					count++;
			}

			return count;
		}

		private static Entity CreateEntityFromSheetEntity(
			SheetEntity e,
			double dx,
			double dy)
		{
			if (e == null)
				return null;

			if (e.HasLineGeometry)
			{
				Line line = new Line(
					ToPoint3d(e.StartPoint.Value, dx, dy),
					ToPoint3d(e.EndPoint.Value, dx, dy));

				return line;
			}

			if (e.HasPolylineGeometry)
			{
				Polyline pl = new Polyline();

				for (int i = 0; i < e.Vertices.Count; i++)
				{
					Point2D p = e.Vertices[i];

					pl.AddVertexAt(
						i,
						new Point2d(p.X + dx, p.Y + dy),
						0.0,
						0.0,
						0.0);
				}

				pl.Closed = e.IsClosed;
				return pl;
			}

			if (e.HasCircleGeometry)
			{
				Circle c = new Circle(
					ToPoint3d(e.CenterPoint.Value, dx, dy),
					Vector3d.ZAxis,
					e.Radius.Value);

				return c;
			}

			if (e.HasArcGeometry)
			{
				Arc arc = new Arc(
					ToPoint3d(e.CenterPoint.Value, dx, dy),
					e.Radius.Value,
					e.StartAngleDeg2D.Value * Math.PI / 180.0,
					e.EndAngleDeg2D.Value * Math.PI / 180.0);

				return arc;
			}

			return null;
		}

		private static Point3d ToPoint3d(
			Point2D p,
			double dx,
			double dy)
		{
			return new Point3d(p.X + dx, p.Y + dy, 0.0);
		}


		[CommandMethod("FLUX_DEBUG_VIEW_ISLAND_CLOSED_LOOP")]
		public void RunClosedLoop()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Editor ed = doc.Editor;
			Database db = doc.Database;

			PromptSelectionOptions pso = new PromptSelectionOptions();
			pso.MessageForAdding =
				"\n복사된 쉬트 프레임 안에서 형상 뷰 영역만 선택하세요: ";

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

					CollectSheetEntities(ent, tr, entities);
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


			AppendLog(ed, "[ViewIsland] Unknown Entities:");

			for (int i = 0; i < set.UnknownEntities.Count; i++)
			{
				SheetEntity e = set.UnknownEntities[i];

				AppendLog(ed, string.Format(
					"  Unknown[{0}] Handle={1}, Kind={2}, EntityType={3}, Layer={4}, Bounds={5}",
					i,
					e.Handle,
					e.Kind,
					e.EntityType,
					e.Layer,
					e.Bounds));
			}

			AppendLog(ed, "[ViewIsland] Geometry Entities:");

			for (int i = 0; i < set.GeometryEntities.Count; i++)
			{
				SheetEntity e = set.GeometryEntities[i];

				AppendLog(ed, string.Format(
					"  Geometry[{0}] Handle={1}, Kind={2}, EntityType={3}, Layer={4}, Bounds={5}",
					i,
					e.Handle,
					e.Kind,
					e.EntityType,
					e.Layer,
					e.Bounds));
			}


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

			ViewIslandRoleClassifier.ClassifyAll(islands);

			ViewIslandDebugDrawer.Draw(
				db,
				ed,
				islands);

			DrawClosedLoopsForIslands(db, ed, islands);
		}

		private static void DrawClosedLoopsForIslands(
			Database db,
			Editor ed,
			List<ViewIsland> islands)
		{
			LoopExtractionOptions loopOptions = new LoopExtractionOptions();

			ViewIslandClosedLoopBuilder builder =
				new ViewIslandClosedLoopBuilder();

			for (int i = 0; i < islands.Count; i++)
			{
				ViewIsland island = islands[i];

				ClosedLoopExtractionResult result =
					builder.Build(island, loopOptions);

				AppendLog(ed, string.Format(
					"[ClosedLoop Island {0}] Segments={1}, ClosedLoops={2}, OpenChains={3}, LargestArea={4:0.###}",
					island.Index,
					result.InputSegments.Count,
					result.ClosedLoops.Count,
					result.OpenChains.Count,
					result.LargestLoopArea));

				ClosedLoopDebugDrawer.Draw(db, ed, island, result);
			}
		}



		[CommandMethod("FLUX_DEBUG_VIEW_ISLANDS")]
		public void Run()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Editor ed = doc.Editor;
			Database db = doc.Database;

			PromptSelectionOptions pso = new PromptSelectionOptions();
			pso.MessageForAdding =
				"\n복사된 쉬트 프레임 안에서 형상 뷰 영역만 선택하세요: ";

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

					CollectSheetEntities(ent, tr, entities);
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


			AppendLog(ed, "[ViewIsland] Unknown Entities:");

			for (int i = 0; i < set.UnknownEntities.Count; i++)
			{
				SheetEntity e = set.UnknownEntities[i];

				AppendLog(ed, string.Format(
					"  Unknown[{0}] Handle={1}, Kind={2}, EntityType={3}, Layer={4}, Bounds={5}",
					i,
					e.Handle,
					e.Kind,
					e.EntityType,
					e.Layer,
					e.Bounds));
			}

			AppendLog(ed, "[ViewIsland] Geometry Entities:");

			for (int i = 0; i < set.GeometryEntities.Count; i++)
			{
				SheetEntity e = set.GeometryEntities[i];

				AppendLog(ed, string.Format(
					"  Geometry[{0}] Handle={1}, Kind={2}, EntityType={3}, Layer={4}, Bounds={5}",
					i,
					e.Handle,
					e.Kind,
					e.EntityType,
					e.Layer,
					e.Bounds));
			}


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

			ViewIslandRoleClassifier.ClassifyAll(islands);

			ViewIslandDebugDrawer.Draw(
				db,
				ed,
				islands);


		}

		internal static void CollectSheetEntities(
			Entity ent,
			Transaction tr,
			List<SheetEntity> results)
		{
			if (ent == null)
				return;

			BlockReference br = ent as BlockReference;
			if (br != null)
			{
				CollectBlockReferenceEntities(
					br,
					br.BlockTransform,
					tr,
					results);
				return;
			}

			SheetEntity se = ConvertToSheetEntity(ent);
			if (se == null)
				return;

			if (SheetEntityReferenceFilter.IsReferenceEntity(se))
				return;

			results.Add(se);
		}

		private static void CollectBlockReferenceEntities(
			BlockReference br,
			Matrix3d accumulatedTransform,
			Transaction tr,
			List<SheetEntity> results)
		{
			if (br == null)
				return;

			BlockTableRecord btr =
				tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;

			if (btr == null)
				return;

			foreach (ObjectId id in btr)
			{
				Entity child = tr.GetObject(id, OpenMode.ForRead) as Entity;
				if (child == null)
					continue;

				BlockReference childBr = child as BlockReference;
				if (childBr != null)
				{
					Matrix3d nestedTransform =
						childBr.BlockTransform.PreMultiplyBy(accumulatedTransform);

					CollectBlockReferenceEntities(
						childBr,
						nestedTransform,
						tr,
						results);

					continue;
				}

				SheetEntity se = ConvertToSheetEntity(child);
				if (se == null)
					continue;

				se.IsFromBlock = true;
				se.IsWorldCoordinate = false;
				se.SourceBlockName = br.Name;
				se.ParentHandle = br.Handle.ToString();
				se.AddBlockPath(br.Name);

				SheetEntityTransformTools.Transform(se, accumulatedTransform);

				if (SheetEntityReferenceFilter.IsReferenceEntity(se))
					continue;

				results.Add(se);
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
