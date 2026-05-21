using System.Collections.Generic;

using Teigha.DatabaseServices;
using Bricscad.EditorInput;
using FluxCad48.Geometry;

namespace FluxCad48.Brics
{
	public static class BricscadSheetContentFinder
	{
		public static List<ObjectId> FindEntitiesInsideBounds(
			Transaction tr,
			Database db,
			Bounds2D frameBounds,
			Editor ed)
		{
			List<ObjectId> result = new List<ObjectId>();

			BlockTable bt =
				(BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

			BlockTableRecord modelSpace =
				(BlockTableRecord)tr.GetObject(
					bt[BlockTableRecord.ModelSpace],
					OpenMode.ForRead);

			foreach (ObjectId id in modelSpace)
			{
				Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

				if (ent == null)
					continue;

				Bounds2D entityBounds =
					BricscadEntityTools.GetEntityBounds(tr, id);

				if (entityBounds == null || !entityBounds.IsValid)
				{
					ed.WriteMessage(
						$"\n[SkipBounds] Type={ent.GetType().Name}, Layer={ent.Layer}");

					continue;
				}

				if (!TryDecideOwnership(ent, entityBounds, frameBounds, ed))
					continue;

				if (!result.Contains(id))
					result.Add(id);
			}

			return result;
		}

		private static bool TryDecideOwnership(
			Entity ent,
			Bounds2D entityBounds,
			Bounds2D frameBounds,
			Editor ed,
			double overlapThreshold = 0.60)
		{
			if (ent == null || entityBounds == null || !entityBounds.IsValid)
				return false;

			if (frameBounds == null || !frameBounds.IsValid)
				return false;

			bool centerInside = frameBounds.ContainsPoint(
				entityBounds.CenterX,
				entityBounds.CenterY);

			double overlapRatio = entityBounds.IntersectionAreaRatio(frameBounds);

			bool keep = centerInside || overlapRatio >= overlapThreshold;

			if (!keep && IsAnnotationEntity(ent))
			{
				double margin =
					System.Math.Max(frameBounds.Width, frameBounds.Height) * 0.03;

				Bounds2D expandedFrame = frameBounds.Expand(margin);

				bool annotationCenterInside =
					expandedFrame.ContainsPoint(
						entityBounds.CenterX,
						entityBounds.CenterY);

				bool annotationIntersects =
					expandedFrame.Intersects(entityBounds);

				keep = annotationCenterInside || annotationIntersects;
			}

			ed.WriteMessage(
				$"\n[Ownership] Type={ent.GetType().Name}, CenterInside={centerInside}, Overlap={overlapRatio:0.000}, Keep={keep}");

			return keep;
		}

		private static bool IsAnnotationEntity(Entity ent)
		{
			return ent is DBText ||
				   ent is MText ||
				   ent is Dimension ||
				   ent is Leader;
		}

		
	}
}