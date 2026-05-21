using System.Collections.Generic;

using Teigha.DatabaseServices;

using FluxCad48.Geometry;

namespace FluxCad48.Bricscad
{
	public static class BricscadSheetContentFinder
	{
		public static List<ObjectId> FindEntitiesInsideBounds(
			Transaction tr,
			Database db,
			Bounds2D frameBounds)
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
					continue;

				if (frameBounds.ContainsBounds(entityBounds))
					result.Add(id);
			}

			return result;
		}
	}
}