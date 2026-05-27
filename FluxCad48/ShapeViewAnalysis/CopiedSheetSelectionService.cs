using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCad48.ShapeViewAnalysis
{
	public static class CopiedSheetSelectionService
	{
		public static CopiedSheetSelectionResult SelectByPickedEntity(
			Database db,
			Transaction tr,
			ObjectId pickedId)
		{
			CopiedSheetSelectionResult result = new CopiedSheetSelectionResult();

			Entity picked = tr.GetObject(pickedId, OpenMode.ForRead, false) as Entity;
			if (picked == null)
			{
				result.ErrorMessage = "선택 개체가 Entity가 아닙니다.";
				return result;
			}

			string sheetCode = TryGetSheetCode(picked);
			if (string.IsNullOrEmpty(sheetCode))
			{
				result.ErrorMessage = "선택 개체에서 SheetCode를 찾지 못했습니다.";
				return result;
			}

			Extents3d pickedExt;
			if (!TryGetExtents(picked, out pickedExt))
			{
				result.ErrorMessage = "선택 개체의 Bounds를 계산하지 못했습니다.";
				return result;
			}

			List<EntityItem> sameCodeItems = CollectEntitiesBySheetCode(db, tr, sheetCode);
			if (sameCodeItems.Count == 0)
			{
				result.ErrorMessage = "같은 SheetCode를 가진 개체가 없습니다.";
				return result;
			}

			List<ObjectId> selectedGroup = FindSpatialGroupContainingPicked(
				sameCodeItems,
				pickedId,
				pickedExt);

			if (selectedGroup.Count == 0)
			{
				result.ErrorMessage = "선택 개체가 속한 공간 그룹을 찾지 못했습니다.";
				return result;
			}

			result.SheetCode = sheetCode;
			result.SelectedIds.AddRange(selectedGroup);

			Extents3d groupBounds;
			Extents3d frameBounds;

			result.HasGroupBounds = TryCalculateGroupBounds(tr, selectedGroup, out groupBounds);
			result.HasFrameBounds = TryFindBestFrameBounds(tr, selectedGroup, out frameBounds);

			if (result.HasGroupBounds)
				result.GroupBounds = groupBounds;

			if (result.HasFrameBounds)
			{
				result.FrameBounds = frameBounds;
			}
			else if (result.HasGroupBounds)
			{
				result.FrameBounds = groupBounds;
				result.HasFrameBounds = true;
			}

			result.Success = true;
			return result;
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

			foreach (EntityItem item in items)
			{
				if (IsInsideOrTouch(groupBounds, item.Bounds, 1.0))
					result.Add(item.Id);
			}

			return result;
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

				if (w < 100 || h < 100)
					continue;

				string typeName = ent.GetType().Name;
				double area = w * h;
				double score = area;

				if (typeName == "BlockReference")
					score += area * 2.0;

				if (typeName == "Polyline" || typeName == "Polyline2d" || typeName == "Polyline3d")
					score += area * 1.0;

				if (score > bestScore)
				{
					bestScore = score;
					frameBounds = ext;
					found = true;
				}
			}

			return found;
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

		public static string TryGetSheetCode(Entity ent)
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

		private sealed class EntityItem
		{
			public ObjectId Id { get; set; }
			public Extents3d Bounds { get; set; }
		}
	}
}