using Bricscad.EditorInput;
using FluxCad48.Geometry;
using FluxCad48.ShapeViewAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using System.Globalization;

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
				se.Text = GetDisplayText(dbText);
				se.TextNormalized = NormalizeText(se.Text);
				se.TextHeight = dbText.Height;
				se.RotationDeg = dbText.Rotation * 180.0 / Math.PI;
				return se;
			}


			//-----------------------------------------
			// MText
			//-----------------------------------------
			MText mText = ent as MText;
			if (mText != null)
			{
				se.Text = GetDisplayText(mText);
				se.TextNormalized = NormalizeText(se.Text);
				se.TextHeight = mText.TextHeight;
				return se;
			}

			//-----------------------------------------
			// Dimension
			//-----------------------------------------
			Dimension dim = ent as Dimension;

			if (dim != null)
			{
				se.Text = GetDimensionDisplayTextSafe(dim);
				se.TextNormalized = NormalizeText(se.Text);
				return se;
			}

			return se;
		}

		private static string GetDimensionDisplayTextSafe(Dimension dim)
		{
			if (dim == null)
				return string.Empty;

			try
			{
				string s = dim.DimensionText ?? string.Empty;

				s = s.Replace("\\X", " ");
				s = s.Replace("\\P", " ");
				s = s.Replace("\r", " ");
				s = s.Replace("\n", " ");
				s = s.Trim();

				if (string.IsNullOrWhiteSpace(s) || s == "<>")
					s = dim.Measurement.ToString("0.###", CultureInfo.InvariantCulture);

				return s.Trim();
			}
			catch
			{
				return string.Empty;
			}
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

		private static string GetDisplayText(DBText dbText)
		{
			return dbText == null || dbText.TextString == null
				? string.Empty
				: dbText.TextString.Trim();
		}

		private static string GetDisplayText(MText mText)
		{
			if (mText == null)
				return string.Empty;

			string s = mText.Text ?? string.Empty;

			if (string.IsNullOrWhiteSpace(s))
				s = mText.Contents ?? string.Empty;

			s = s.Replace("\\P", " ");
			s = s.Replace("\\p", " ");
			s = s.Replace("\r", " ");
			s = s.Replace("\n", " ");

			return s.Trim();
		}

		private static string NormalizeText(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return string.Empty;

			return text.Trim();
		}

		private static void AppendLog(Editor ed, string message)
		{
			ed.WriteMessage("\n" + message);
		}
	}
}
