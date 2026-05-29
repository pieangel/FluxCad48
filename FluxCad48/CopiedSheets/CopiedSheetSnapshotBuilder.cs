using FluxCad48.ShapeViewAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using Bricscad.EditorInput;

namespace FluxCad48.CopiedSheets
{
	public static class CopiedSheetSnapshotBuilder
	{
		public static void BuildCopiedSheetEntitySnapshot(
			Transaction tr,
			CopiedSheetRecord record)
		{
			if (tr == null)
				throw new ArgumentNullException(nameof(tr));

			if (record == null)
				throw new ArgumentNullException(nameof(record));

			record.CopiedEntities.Clear();

			foreach (ObjectId id in record.CopiedEntityIds)
			{
				if (id.IsNull || id.IsErased)
					continue;

				Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
				if (ent == null)
					continue;

				SheetEntity sheetEntity = CreateSheetEntity(ent);

				if (sheetEntity != null)
					record.CopiedEntities.Add(sheetEntity);
			}
		}

		private static SheetEntity CreateSheetEntity(Entity ent)
		{
			if (ent == null)
				return null;

			SheetEntity se = new SheetEntity();

			se.Handle = ent.Handle.ToString();
			se.EntityType = ent.GetType().Name;
			se.Layer = ent.Layer;

			return se;
		}

		private static void AppendLog(Editor ed, string message)
		{
			ed.WriteMessage("\n" + message);
		}
	}
}
