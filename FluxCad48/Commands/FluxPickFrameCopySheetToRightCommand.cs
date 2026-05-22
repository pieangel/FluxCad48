using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using FluxCad48.Brics;
using FluxCad48.Geometry;
using FluxCad48.Sheets;
using System;
using System.Collections.Generic;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace FluxCad48.Commands
{
	public class FluxPickFrameCopySheetToRightCommand
	{
		private const string CopiedLayerName = "FLUX_COPIED";
		private const string MarkerLayerName = "FLUX_MARKER";

		[CommandMethod("FLUX_PICK_FRAME_COPY_SHEET_TO_RIGHT")]
		public void FluxPickFrameCopySheetToRight()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			PromptEntityOptions peo = new PromptEntityOptions(
				"\n오른쪽으로 복사할 쉬트의 프레임을 클릭하세요: ");

			peo.AllowNone = false;

			PromptEntityResult per = ed.GetEntity(peo);

			if (per.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n프레임 선택이 완료되지 않았습니다.");
				return;
			}

			ObjectId frameId = per.ObjectId;

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				Entity frameEntity = tr.GetObject(frameId, OpenMode.ForRead) as Entity;

				if (frameEntity == null)
				{
					ed.WriteMessage("\n선택한 객체가 Entity가 아닙니다.");
					return;
				}

				Bounds2D frameBounds = BricscadEntityTools.GetEntityBounds(tr, frameId);

				if (frameBounds == null || !frameBounds.IsValid)
				{
					ed.WriteMessage("\n선택한 프레임의 Bounds를 계산하지 못했습니다.");
					return;
				}

				Bounds2D drawingBounds = BricscadEntityTools.GetModelSpaceBounds(tr, db);

				if (drawingBounds == null || !drawingBounds.IsValid)
				{
					ed.WriteMessage("\n전체 도면 Bounds를 계산하지 못했습니다.");
					return;
				}

				ed.WriteMessage(
					"\n[FramePick] 선택 객체 Type=" + frameEntity.GetType().Name +
					": WorldBounds를 기준으로 ownership을 판정합니다.");

				List<WorldEntityInfo> worldInfos =
					BricscadEntityTools.CollectWorldEntitiesDeep(tr, db);

				ed.WriteMessage(
					"\n[WorldOwnership] WorldEntityInfo Count=" + worldInfos.Count);

				List<WorldEntityInfo> acceptedInfos =
					CollectOwnedWorldEntities(
						tr,
						ed,
						worldInfos,
						frameBounds);

				if (acceptedInfos.Count == 0)
				{
					ed.WriteMessage("\n복사할 내부 객체를 찾지 못했습니다.");
					return;
				}

				PrintWorldEntitySummary(ed, acceptedInfos);

				ed.WriteMessage(
					"\n[FrameBounds] Width=" + frameBounds.Width +
					", Height=" + frameBounds.Height);

				ed.WriteMessage(
					"\n[FrameEntity] Type=" + frameEntity.GetType().Name +
					", Layer=" + frameEntity.Layer +
					", Bounds=" + frameBounds.ToString());

				List<SheetRegion> sheets = new List<SheetRegion>();

				SheetRegion sheet = new SheetRegion();
				sheet.Index = 0;
				sheet.Bounds = frameBounds;
				sheets.Add(sheet);

				SheetArrangeOptions options = new SheetArrangeOptions();
				SheetArranger arranger = new SheetArranger();

				List<SheetPlacement> placements =
					arranger.CreateHorizontalPlacements(
						sheets,
						drawingBounds,
						options);

				BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
				BlockTableRecord modelSpace =
					(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

				BricscadEntityTools.EnsureLayer(tr, db, CopiedLayerName);
				BricscadEntityTools.EnsureLayer(tr, db, MarkerLayerName);

				foreach (SheetPlacement placement in placements)
				{
					int clonedCount = 0;
					int failedCount = 0;

					Vector3d displacement =
						new Vector3d(placement.MoveX, placement.MoveY, 0);

					foreach (WorldEntityInfo info in acceptedInfos)
					{
						Entity clonedEntity =
							BricscadEntityTools.WorldCloneEntityToModelSpace(
								tr,
								db,
								modelSpace,
								info,
								displacement,
								CopiedLayerName);

						if (clonedEntity == null)
						{
							failedCount++;
							continue;
						}

						clonedCount++;
					}

					ed.WriteMessage(
						"\n[WorldClone] Cloned=" + clonedCount +
						", Failed=" + failedCount +
						", Move=(" + placement.MoveX.ToString("0.###") +
						"," + placement.MoveY.ToString("0.###") + ")");

					if (options.DrawYellowMarkerOnSource)
					{
						Polyline marker =
							BricscadEntityTools.CreateRectanglePolyline(placement.SourceBounds);

						marker.Color = Color.FromColorIndex(ColorMethod.ByAci, 2);
						marker.LineWeight = LineWeight.LineWeight050;
						marker.Layer = MarkerLayerName;

						modelSpace.AppendEntity(marker);
						tr.AddNewlyCreatedDBObject(marker, true);
					}
				}

				tr.Commit();

				ed.WriteMessage(
					"\nFLUX_PICK_FRAME_COPY_SHEET_TO_RIGHT 완료: WorldClone 방식으로 선택 프레임 내부 쉬트를 오른쪽에 복사했습니다.");
			}
		}

		private static List<WorldEntityInfo> CollectOwnedWorldEntities(
			Transaction tr,
			Editor ed,
			List<WorldEntityInfo> worldInfos,
			Bounds2D frameBounds)
		{
			List<WorldEntityInfo> result = new List<WorldEntityInfo>();
			HashSet<ObjectId> directObjectIds = new HashSet<ObjectId>();

			int tested = 0;
			int accepted = 0;
			int skippedBlockContainer = 0;
			int duplicateDirectSkipped = 0;
			int ownerRejected = 0;

			foreach (WorldEntityInfo info in worldInfos)
			{
				tested++;

				if (info.WorldBounds == null || !info.WorldBounds.IsValid)
					continue;

				double overlapArea =
					frameBounds.GetIntersectionArea(info.WorldBounds);

				double ratioByEntity =
					info.WorldBounds.Area > 0
						? overlapArea / info.WorldBounds.Area
						: 0.0;

				bool centerInside =
					frameBounds.ContainsPoint(info.WorldBounds.CenterPoint);

				bool keep =
					centerInside || ratioByEntity >= 0.50;

				if (!keep)
					continue;

				if (info.EntityType == "BlockReference")
				{
					skippedBlockContainer++;
					continue;
				}

				if (info.BlockDepth > 0 && !info.OwnerBlockReferenceId.IsNull)
				{
					Entity ownerBrEntity =
						tr.GetObject(info.OwnerBlockReferenceId, OpenMode.ForRead) as Entity;

					Bounds2D ownerBounds =
						ownerBrEntity != null
							? BricscadEntityTools.GetEntityBoundsSafe(ownerBrEntity)
							: null;

					double ownerOverlapArea =
						ownerBounds != null
							? frameBounds.GetIntersectionArea(ownerBounds)
							: 0.0;

					double ownerRatio =
						ownerBounds != null && ownerBounds.Area > 0
							? ownerOverlapArea / ownerBounds.Area
							: 0.0;

					bool ownerCenterInside =
						ownerBounds != null &&
						frameBounds.ContainsPoint(ownerBounds.CenterPoint);

					if (!(ownerCenterInside || ownerRatio >= 0.80))
					{
						ownerRejected++;
						continue;
					}
				}
				else
				{
					if (directObjectIds.Contains(info.SourceId))
					{
						duplicateDirectSkipped++;
						continue;
					}

					directObjectIds.Add(info.SourceId);
				}

				result.Add(info);
				accepted++;
			}

			ed.WriteMessage(
				"\n[WorldOwnership] Tested=" + tested +
				", Accepted=" + accepted +
				", SkippedBlockContainer=" + skippedBlockContainer +
				", DuplicateDirectSkipped=" + duplicateDirectSkipped +
				", OwnerRejected=" + ownerRejected +
				", FinalWorldInfos=" + result.Count);

			return result;
		}

		private static void PrintWorldEntitySummary(Editor ed, List<WorldEntityInfo> infos)
		{
			Dictionary<string, int> typeCounts = new Dictionary<string, int>();
			Dictionary<string, int> layerCounts = new Dictionary<string, int>();

			foreach (WorldEntityInfo info in infos)
			{
				string type = info.EntityType ?? "(null)";
				string layer = info.Layer ?? "(null)";

				if (!typeCounts.ContainsKey(type))
					typeCounts[type] = 0;

				typeCounts[type]++;

				if (!layerCounts.ContainsKey(layer))
					layerCounts[layer] = 0;

				layerCounts[layer]++;
			}

			ed.WriteMessage("\n[ContentSummary] Type Counts:");

			foreach (KeyValuePair<string, int> kv in typeCounts)
			{
				ed.WriteMessage(
					"\n  Type=" + kv.Key + ", Count=" + kv.Value);
			}

			ed.WriteMessage("\n[ContentSummary] Layer Counts:");

			foreach (KeyValuePair<string, int> kv in layerCounts)
			{
				ed.WriteMessage(
					"\n  Layer=" + kv.Key + ", Count=" + kv.Value);
			}
		}

		[CommandMethod("FLUX_DEBUG_WORLD_ENTITIES")]
		public void FluxDebugWorldEntities()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				List<WorldEntityInfo> infos =
					BricscadEntityTools.CollectWorldEntitiesDeep(tr, db);

				ed.WriteMessage($"\n[WorldEntities] Count={infos.Count}");

				int invalid = 0;
				int insideBlock = 0;

				foreach (WorldEntityInfo info in infos)
				{
					if (info.WorldBounds == null || !info.WorldBounds.IsValid)
						invalid++;

					if (info.IsInsideBlock)
						insideBlock++;
				}

				ed.WriteMessage($"\n[WorldEntities] InsideBlock={insideBlock}, InvalidBounds={invalid}");

				for (int i = 0; i < Math.Min(50, infos.Count); i++)
				{
					WorldEntityInfo info = infos[i];

					ed.WriteMessage(
						$"\n[{i}] Type={info.EntityType}, Layer={info.Layer}, Depth={info.BlockDepth}, World={FormatBoundsForDebug(info.WorldBounds)}");
				}

				tr.Commit();
			}
		}

		private static string FormatBoundsForDebug(Bounds2D b)
		{
			if (b == null || !b.IsValid)
				return "(invalid)";

			return $"Min=({b.MinX:0.###},{b.MinY:0.###}) Max=({b.MaxX:0.###},{b.MaxY:0.###}) W={b.Width:0.###} H={b.Height:0.###}";
		}
	}
}
