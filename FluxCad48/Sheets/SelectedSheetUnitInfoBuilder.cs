using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using FluxCad48.Geometry;
using FluxCad48.Brics;

namespace FluxCad48.Sheets
{
	public static class SelectedSheetUnitInfoBuilder
	{
		public static List<SelectedSheetUnitInfo> BuildFromEntities(
			Transaction tr,
			IEnumerable<ObjectId> ids)
		{
			var result = new List<SelectedSheetUnitInfo>();

			foreach (ObjectId id in ids)
			{
				Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
				if (ent == null)
					continue;

				SelectedSheetUnitInfo info = BuildFromEntity(ent);
				if (info == null)
					continue;

				result.Add(info);
			}

			return result;
		}

		public static SelectedSheetUnitInfo BuildFromEntity(Entity ent)
		{
			if (ent == null)
				return null;

			Bounds2D bounds = BricscadEntityTools.GetEntityBounds(ent);
			if (bounds == null)
				return null;

			var info = new SelectedSheetUnitInfo();

			info.Id = ent.ObjectId;
			info.HandleText = ent.Handle.ToString();
			info.TypeName = ent.GetType().Name;
			info.Layer = ent.Layer ?? "";

			info.BoundsWcs = bounds;
			info.CenterWcs = bounds.Center;

			info.IsBlockReference = ent is BlockReference;
			info.IsDimensionLike = ent is Dimension;
			info.IsTextLike = IsTextEntity(ent);
			info.IsLineLike = IsLineEntity(ent);

			info.BlockName = GetBlockName(ent);
			info.Text = GetText(ent);
			info.NormalizedText = NormalizeText(info.Text);

			info.IsCenterLineLike = IsCenterLineLike(ent.Layer, info.TypeName);
			info.IsHiddenLineLike = IsHiddenLineLike(ent.Layer, info.TypeName);

			return info;
		}

		private static bool IsTextEntity(Entity ent)
		{
			return ent is DBText || ent is MText;
		}

		private static bool IsLineEntity(Entity ent)
		{
			return ent is Line ||
				   ent is Polyline ||
				   ent is Polyline2d ||
				   ent is Polyline3d ||
				   ent is Arc ||
				   ent is Circle;
		}

		private static string GetBlockName(Entity ent)
		{
			BlockReference br = ent as BlockReference;
			if (br == null)
				return "";

			try
			{
				return br.Name ?? "";
			}
			catch
			{
				return "";
			}
		}

		private static string GetText(Entity ent)
		{
			DBText dbText = ent as DBText;
			if (dbText != null)
				return dbText.TextString ?? "";

			MText mText = ent as MText;
			if (mText != null)
				return mText.Contents ?? "";

			return "";
		}

		private static string NormalizeText(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return "";

			return text
				.Replace(" ", "")
				.Replace("\t", "")
				.Replace("\r", "")
				.Replace("\n", "")
				.Trim()
				.ToUpperInvariant();
		}

		private static bool IsCenterLineLike(string layer, string typeName)
		{
			string s = ((layer ?? "") + " " + (typeName ?? "")).ToUpperInvariant();

			return s.Contains("CENTER") ||
				   s.Contains("CENTRE") ||
				   s.Contains("CENTERLINE") ||
				   s.Contains("CENTER_LINE") ||
				   s.Contains("CL") ||
				   s.Contains("중심");
		}

		private static bool IsHiddenLineLike(string layer, string typeName)
		{
			string l = (layer ?? "").Trim().ToUpperInvariant();
			string s = (l + " " + (typeName ?? "")).ToUpperInvariant();

			return l == "HL" ||
				   l.Contains("HIDDEN") ||
				   l.Contains("HIDE") ||
				   l.Contains("DASH") ||
				   l.Contains("HID") ||
				   l.Contains("숨은") ||
				   s.Contains("HIDDEN");
		}
	}
}