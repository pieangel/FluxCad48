using Bricscad.EditorInput;
using FluxCad48.Geometry;
using FluxCad48.ShapeViewAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCad48.CopiedSheets
{
	public static class CopiedSheetSnapshotBuilder
	{
		public static void BuildCopiedSheetEntitySnapshot(
			Transaction tr,
			CopiedSheetRecord record,
			Editor ed)
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

			Extents3d? ex = ent.GeometricExtents;

			if (ex.HasValue)
			{
				se.Bounds = new Bounds2D(
					ex.Value.MinPoint.X,
					ex.Value.MinPoint.Y,
					ex.Value.MaxPoint.X,
					ex.Value.MaxPoint.Y);
			}

			//-----------------------------------------
			// DBText
			//-----------------------------------------
			DBText dbText = ent as DBText;

			if (dbText != null)
			{
				se.Text = dbText.TextString;
				se.TextHeight = dbText.Height;
				se.RotationDeg =
					dbText.Rotation * 180.0 / Math.PI;

				return se;
			}

			//-----------------------------------------
			// MText
			//-----------------------------------------
			MText mt = ent as MText;

			if (mt != null)
			{
				se.Text = mt.Text;
				se.TextHeight = mt.TextHeight;

				return se;
			}

			//-----------------------------------------
			// Dimension
			//-----------------------------------------
			Dimension dim = ent as Dimension;

			if (dim != null)
			{
				se.Text = dim.DimensionText;

				return se;
			}

			return se;
		}

		public static List<SheetEntity> GetTextEntities(
			CopiedSheetRecord record)
		{
			List<SheetEntity> result =
				new List<SheetEntity>();

			foreach (SheetEntity ent in record.CopiedEntities)
			{
				if (string.IsNullOrWhiteSpace(ent.Text))
					continue;

				result.Add(ent);
			}

			return result;
		}

		private static void AppendLog(Editor ed, string message)
		{
			ed.WriteMessage("\n" + message);
		}
	}
}
