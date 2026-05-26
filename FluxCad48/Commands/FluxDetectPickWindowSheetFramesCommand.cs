using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using FluxCad48.Geometry;
using FluxCad48.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace FluxCad48.Commands
{
	public class FluxDetectPickWindowSheetFramesCommand
	{
		[CommandMethod("FLUX_DETECT_PICK_WINDOW_SHEET_FRAMES")]
		public void FluxDetectPickWindowSheetFrames()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			PromptPointResult ppr1 =
				ed.GetPoint("\n쉬트 프레임 탐지 영역의 첫 번째 점을 지정하세요: ");

			if (ppr1.Status != PromptStatus.OK)
				return;

			PromptCornerOptions pco = new PromptCornerOptions(
				"\n반대쪽 점을 지정하세요: ",
				ppr1.Value);

			PromptPointResult ppr2 = ed.GetCorner(pco);

			if (ppr2.Status != PromptStatus.OK)
				return;

			Point3d p1 = ppr1.Value;
			Point3d p2 = ppr2.Value;

			Point3d min = new Point3d(
				Math.Min(p1.X, p2.X),
				Math.Min(p1.Y, p2.Y),
				0);

			Point3d max = new Point3d(
				Math.Max(p1.X, p2.X),
				Math.Max(p1.Y, p2.Y),
				0);

			Bounds2D pickBounds = new Bounds2D(
				min.X,
				min.Y,
				max.X,
				max.Y);

			ed.WriteMessage($"\n[PickWindowFrame] PickBounds={pickBounds}");

			PromptSelectionResult psr =
				ed.SelectCrossingWindow(min, max);

			if (psr.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n[PickWindowFrame] 선택된 객체가 없습니다.");
				return;
			}

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				var selectedEntities = new List<Entity>();
				var selectedIds = new List<ObjectId>();

				foreach (SelectedObject so in psr.Value)
				{
					if (so == null || so.ObjectId.IsNull)
						continue;

					Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;

					if (ent == null)
						continue;

					selectedEntities.Add(ent);
					selectedIds.Add(so.ObjectId);
				}

				ed.WriteMessage(
					$"\n[PickWindowFrame] SelectedEntities={selectedEntities.Count}");

				var units = SelectedSheetUnitInfoBuilder.BuildFromEntities(
					tr,
					selectedIds);

				ed.WriteMessage(
					$"\n[PickWindowFrame] Units={units.Count}, " +
					$"Dim={units.Count(u => u.IsDimensionLike)}, " +
					$"Center={units.Count(u => u.IsCenterLineLike)}, " +
					$"Hidden={units.Count(u => u.IsHiddenLineLike)}");

				var frames = SheetFrameDetector.Detect(
					selectedEntities,
					pickBounds,
					ed);

				ed.WriteMessage(
					$"\n[PickWindowFrame] FinalDetectedFrames={frames.Count}");

				int index = 1;

				foreach (var frame in frames)
				{
					int dimInside = units.Count(u =>
						u.IsDimensionLike &&
						frame.Bounds.Contains(u.CenterWcs));

					int centerInside = units.Count(u =>
						u.IsCenterLineLike &&
						frame.Bounds.Contains(u.CenterWcs));

					int hiddenInside = units.Count(u =>
						u.IsHiddenLineLike &&
						frame.Bounds.Contains(u.CenterWcs));

					bool hasEssentialFeatures =
						dimInside > 0 ||
						centerInside > 0 ||
						hiddenInside > 0;

					double containedRatio =
						frame.Bounds.ContainedRatioIn(pickBounds);

					ed.WriteMessage(
						$"\n[Frame {index}] " +
						$"Handle={frame.Handle}, " +
						$"Type={frame.EntityType}, " +
						$"Layer={frame.Layer}, " +
						$"Bounds={frame.Bounds}, " +
						$"PickContainedRatio={containedRatio:0.000}, " +
						$"Inside={frame.InsideEntityCount}, " +
						$"Dim={dimInside}, " +
						$"Center={centerInside}, " +
						$"Hidden={hiddenInside}, " +
						$"Essential={hasEssentialFeatures}, " +
						$"Score={frame.Score:0.00}");

					index++;
				}

				tr.Commit();
			}
		}
	}
}