using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using FluxCad48.ShapeViewAnalysis;
using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace FluxCad48.Commands
{
	public class CopiedSheetSelectionCommands
	{
		[CommandMethod("FLUX_SELECT_COPIED_SHEET_BY_PICK")]
		public void SelectCopiedSheetByPick()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Editor ed = doc.Editor;
			Database db = doc.Database;

			PromptEntityOptions peo = new PromptEntityOptions(
				"\n복사된 쉬트 프레임 안의 아무 개체나 선택하세요: ");

			PromptEntityResult per = ed.GetEntity(peo);

			if (per.Status != PromptStatus.OK)
			{
				AppendLog(ed, "[SelectCopiedSheet] 선택이 취소되었습니다.");
				return;
			}

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				CopiedSheetSelectionResult result =
					CopiedSheetSelectionService.SelectByPickedEntity(db, tr, per.ObjectId);

				if (!result.Success)
				{
					AppendLog(ed, "[SelectCopiedSheet] 실패: " + result.ErrorMessage);
					return;
				}

				ed.SetImpliedSelection(result.SelectedIds.ToArray());

				AppendLog(ed, "[SelectCopiedSheet] Picked SheetCode=" + result.SheetCode);
				AppendLog(ed, "[SelectCopiedSheet] 선택 완료. Group Entity Count=" + result.SelectedIds.Count);

				if (result.HasGroupBounds)
					AppendBounds(ed, "[SelectCopiedSheet] GroupBounds", result.GroupBounds);

				if (result.HasFrameBounds)
					AppendBounds(ed, "[SelectCopiedSheet] FrameBounds", result.FrameBounds);

				tr.Commit();
			}
		}

		[CommandMethod("FLUX_SELECT_COPIED_SHEET_BY_PICK_LEGACY")]
		public void SelectCopiedSheetByPick_Legacy()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Editor ed = doc.Editor;
			Database db = doc.Database;

			PromptEntityOptions peo = new PromptEntityOptions(
				"\n복사된 쉬트 프레임 안의 아무 개체나 선택하세요: ");

			PromptEntityResult per = ed.GetEntity(peo);

			if (per.Status != PromptStatus.OK)
			{
				AppendLog(ed, "[SelectCopiedSheet] 선택이 취소되었습니다.");
				return;
			}

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				Entity picked = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Entity;

				if (picked == null)
				{
					AppendLog(ed, "[SelectCopiedSheet] 선택 개체가 Entity가 아닙니다.");
					return;
				}

				string sheetCode = TryGetSheetCode(picked);

				if (string.IsNullOrEmpty(sheetCode))
				{
					AppendLog(ed, "[SelectCopiedSheet] 선택 개체에서 SheetCode를 찾지 못했습니다.");
					DumpXData(ed, picked);
					return;
				}

				Extents3d pickedExt;
				if (!TryGetExtents(picked, out pickedExt))
				{
					AppendLog(ed, "[SelectCopiedSheet] 선택 개체의 Bounds를 계산하지 못했습니다.");
					return;
				}

				List<EntityItem> sameCodeItems = CollectEntitiesBySheetCode(db, tr, sheetCode);

				AppendLog(ed, "[SelectCopiedSheet] Picked SheetCode=" + sheetCode);
				AppendLog(ed, "[SelectCopiedSheet] SameCode Entity Count=" + sameCodeItems.Count);

				if (sameCodeItems.Count == 0)
				{
					AppendLog(ed, "[SelectCopiedSheet] 같은 SheetCode를 가진 개체가 없습니다.");
					return;
				}

				List<ObjectId> selectedGroup = FindSpatialGroupContainingPicked(
					sameCodeItems,
					per.ObjectId,
					pickedExt);

				if (selectedGroup.Count == 0)
				{
					AppendLog(ed, "[SelectCopiedSheet] 선택 개체가 속한 공간 그룹을 찾지 못했습니다.");
					return;
				}

				ed.SetImpliedSelection(selectedGroup.ToArray());

				AppendLog(ed, "[SelectCopiedSheet] 선택 완료. Group Entity Count=" + selectedGroup.Count);

				Extents3d groupBounds;
				Extents3d frameBounds;

				bool hasGroupBounds = TryCalculateGroupBounds(tr, selectedGroup, out groupBounds);
				bool hasFrameBounds = TryFindBestFrameBounds(tr, selectedGroup, out frameBounds);

				if (hasGroupBounds)
				{
					AppendBounds(ed, "[SelectCopiedSheet] GroupBounds", groupBounds);
				}

				if (hasFrameBounds)
				{
					AppendBounds(ed, "[SelectCopiedSheet] FrameBounds", frameBounds);
				}
				else if (hasGroupBounds)
				{
					frameBounds = groupBounds;
					AppendLog(ed, "[SelectCopiedSheet] FrameBounds를 찾지 못해 GroupBounds를 사용합니다.");
				}
				else
				{
					AppendLog(ed, "[SelectCopiedSheet] Bounds 계산 실패");
				}

				tr.Commit();
			}
		}

		private static void AppendBounds(Editor ed, string label, Extents3d b)
		{
			AppendLog(ed,
				label + "=" +
				"Min=(" + b.MinPoint.X.ToString("0.##") + "," +
						  b.MinPoint.Y.ToString("0.##") + ") " +
				"Max=(" + b.MaxPoint.X.ToString("0.##") + "," +
						  b.MaxPoint.Y.ToString("0.##") + ") " +
				"W=" + (b.MaxPoint.X - b.MinPoint.X).ToString("0.##") +
				", H=" + (b.MaxPoint.Y - b.MinPoint.Y).ToString("0.##"));
		}

		private static bool TryFindBestFrameBounds(
	Transaction tr,
	List<ObjectId> ids,
	out Extents3d frameBounds)
		{
			frameBounds = new Extents3d();

			bool found = false;
			double bestScore = double.MinValue;

			foreach (ObjectId id in ids)
			{
				Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
				if (ent == null)
					continue;

				Extents3d ext;
				if (!TryGetExtents(ent, out ext))
					continue;

				double w = ext.MaxPoint.X - ext.MinPoint.X;
				double h = ext.MaxPoint.Y - ext.MinPoint.Y;

				if (w <= 0 || h <= 0)
					continue;

				string typeName = ent.GetType().Name;

				double area = w * h;
				double score = area;

				if (typeName == "BlockReference")
					score += area * 2.0;

				if (typeName == "Polyline" || typeName == "Polyline2d" || typeName == "Polyline3d")
					score += area * 1.0;

				// 너무 작은 내부 부품/텍스트 박스 배제
				if (w < 100 || h < 100)
					continue;

				if (score > bestScore)
				{
					bestScore = score;
					frameBounds = ext;
					found = true;
				}
			}

			return found;
		}


		private static List<EntityItem> CollectEntitiesBySheetCode(
			Database db,
			Transaction tr,
			string sheetCode)
		{
			List<EntityItem> result = new List<EntityItem>();

			BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
			BlockTableRecord ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

			foreach (ObjectId id in ms)
			{
				Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
				if (ent == null)
					continue;

				string code = TryGetSheetCode(ent);

				if (code != sheetCode)
					continue;

				Extents3d ext;
				if (!TryGetExtents(ent, out ext))
					continue;

				result.Add(new EntityItem
				{
					Id = id,
					Bounds = ext
				});
			}

			return result;
		}

		private static List<ObjectId> FindSpatialGroupContainingPicked(
			List<EntityItem> items,
			ObjectId pickedId,
			Extents3d pickedExt)
		{
			List<ObjectId> result = new List<ObjectId>();

			Point3d pickedCenter = GetCenter(pickedExt);

			double nearestDist = double.MaxValue;
			EntityItem nearest = null;

			foreach (EntityItem item in items)
			{
				if (item.Id == pickedId)
				{
					nearest = item;
					break;
				}

				double d = GetDistance(GetCenter(item.Bounds), pickedCenter);
				if (d < nearestDist)
				{
					nearestDist = d;
					nearest = item;
				}
			}

			if (nearest == null)
				return result;

			Extents3d groupBounds = nearest.Bounds;

			// 1차 기준: 선택 개체 주변 같은 SheetCode 묶음의 전체 Bounds를 점진적으로 확장
			bool changed = true;
			int guard = 0;

			while (changed && guard < 20)
			{
				changed = false;
				guard++;

				foreach (EntityItem item in items)
				{
					if (IntersectsOrNear(groupBounds, item.Bounds, 300.0))
					{
						Extents3d before = groupBounds;
						groupBounds.AddExtents(item.Bounds);

						if (!ExtentsAlmostEqual(before, groupBounds))
							changed = true;
					}
				}
			}

			// 2차 기준: 확정된 그룹 Bounds 안에 들어오는 같은 SheetCode 개체만 선택
			foreach (EntityItem item in items)
			{
				if (IsInsideOrTouch(groupBounds, item.Bounds, 1.0))
					result.Add(item.Id);
			}

			return result;
		}

		private static string TryGetSheetCode(Entity ent)
		{
			ResultBuffer rb = ent.XData;
			if (rb == null)
				return "";

			TypedValue[] values = rb.AsArray();

			for (int i = 0; i < values.Length; i++)
			{
				if (values[i].TypeCode == 1001 &&
					Convert.ToString(values[i].Value) == "FLUX_SHEET")
				{
					if (i + 1 < values.Length && values[i + 1].TypeCode == 1000)
						return Convert.ToString(values[i + 1].Value).Trim();
				}
			}

			return "";
		}

		private static bool TryGetExtents(Entity ent, out Extents3d ext)
		{
			try
			{
				ext = ent.GeometricExtents;
				return true;
			}
			catch
			{
				ext = new Extents3d();
				return false;
			}
		}

		private static bool IntersectsOrNear(Extents3d a, Extents3d b, double gap)
		{
			return !(a.MaxPoint.X + gap < b.MinPoint.X ||
					 a.MinPoint.X - gap > b.MaxPoint.X ||
					 a.MaxPoint.Y + gap < b.MinPoint.Y ||
					 a.MinPoint.Y - gap > b.MaxPoint.Y);
		}

		private static bool IsInsideOrTouch(Extents3d outer, Extents3d inner, double tol)
		{
			return inner.MinPoint.X >= outer.MinPoint.X - tol &&
				   inner.MaxPoint.X <= outer.MaxPoint.X + tol &&
				   inner.MinPoint.Y >= outer.MinPoint.Y - tol &&
				   inner.MaxPoint.Y <= outer.MaxPoint.Y + tol;
		}

		private static Point3d GetCenter(Extents3d ext)
		{
			return new Point3d(
				(ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
				(ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
				0);
		}

		private static double GetDistance(Point3d a, Point3d b)
		{
			double dx = a.X - b.X;
			double dy = a.Y - b.Y;
			return Math.Sqrt(dx * dx + dy * dy);
		}

		private static bool ExtentsAlmostEqual(Extents3d a, Extents3d b)
		{
			const double tol = 0.0001;

			return Math.Abs(a.MinPoint.X - b.MinPoint.X) < tol &&
				   Math.Abs(a.MinPoint.Y - b.MinPoint.Y) < tol &&
				   Math.Abs(a.MaxPoint.X - b.MaxPoint.X) < tol &&
				   Math.Abs(a.MaxPoint.Y - b.MaxPoint.Y) < tol;
		}

		private static void DumpXData(Editor ed, Entity ent)
		{
			ResultBuffer rb = ent.XData;
			if (rb == null)
			{
				AppendLog(ed, "[XData] 없음");
				return;
			}

			foreach (TypedValue tv in rb)
			{
				AppendLog(ed, "[XData] Type=" + tv.TypeCode + ", Value=" + Convert.ToString(tv.Value));
			}
		}

		private static void AppendLog(Editor ed, string message)
		{
			ed.WriteMessage("\n" + message);
		}

		private sealed class EntityItem
		{
			public ObjectId Id { get; set; }
			public Extents3d Bounds { get; set; }
		}

		private static bool TryCalculateGroupBounds(
	Transaction tr,
	List<ObjectId> ids,
	out Extents3d bounds)
		{
			bounds = new Extents3d();
			bool hasBounds = false;

			foreach (ObjectId id in ids)
			{
				Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
				if (ent == null)
					continue;

				Extents3d ext;
				if (!TryGetExtents(ent, out ext))
					continue;

				if (!hasBounds)
				{
					bounds = ext;
					hasBounds = true;
				}
				else
				{
					bounds.AddExtents(ext);
				}
			}

			return hasBounds;
		}

	}
}