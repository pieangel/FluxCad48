using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FluxCad48.ShapeViewAnalysis;

namespace FluxCad48.CopiedSheets
{
	public static class CopiedSheetMetadataExtractor
	{
		public static SheetMetadata Extract(CopiedSheetRecord record)
		{
			SheetMetadata metadata = new SheetMetadata();

			if (record == null)
				return metadata;

			List<SheetEntity> texts = GetTextEntities(record);

			metadata.ExtraQuantityText = FindSetText(texts);
			metadata.QuantityText = FindQuantityText(texts);

			int qty;
			if (int.TryParse(metadata.QuantityText, out qty))
				metadata.Quantity = qty;

			record.Metadata = metadata;

			return metadata;
		}

		private static List<SheetEntity> GetTextEntities(CopiedSheetRecord record)
		{
			List<SheetEntity> result = new List<SheetEntity>();

			foreach (SheetEntity ent in record.CopiedEntities)
			{
				if (ent == null)
					continue;

				if (string.IsNullOrWhiteSpace(ent.Text))
					continue;

				result.Add(ent);
			}

			return result;
		}

		private static string FindSetText(List<SheetEntity> texts)
		{
			Regex regex = new Regex(
				@"\b\d+\s*[\*xX]\s*\d+\s*SET\b|\b\d+\s*SET\b",
				RegexOptions.IgnoreCase);

			foreach (SheetEntity ent in texts)
			{
				string text = Normalize(ent.Text);

				Match m = regex.Match(text);
				if (m.Success)
					return m.Value;
			}

			return "";
		}

		private static string FindQuantityText(List<SheetEntity> texts)
		{
			Regex regex = new Regex(
				@"\bQ\s*'?TY\b|\bQTY\b",
				RegexOptions.IgnoreCase);

			for (int i = 0; i < texts.Count; i++)
			{
				string text = Normalize(texts[i].Text);

				if (!regex.IsMatch(text))
					continue;

				SheetEntity nearest = FindNearestValueText(texts, texts[i]);

				if (nearest != null)
					return Normalize(nearest.Text);
			}

			return "";
		}

		private static SheetEntity FindNearestValueText(
			List<SheetEntity> texts,
			SheetEntity label)
		{
			if (label == null || label.Bounds == null)
				return null;

			SheetEntity best = null;
			double bestScore = double.MaxValue;

			double labelCx = (label.Bounds.MinX + label.Bounds.MaxX) * 0.5;
			double labelCy = (label.Bounds.MinY + label.Bounds.MaxY) * 0.5;

			foreach (SheetEntity ent in texts)
			{
				if (ent == label)
					continue;

				if (ent.Bounds == null)
					continue;

				string value = Normalize(ent.Text);

				if (!Regex.IsMatch(value, @"^\d+$"))
					continue;

				double cx = (ent.Bounds.MinX + ent.Bounds.MaxX) * 0.5;
				double cy = (ent.Bounds.MinY + ent.Bounds.MaxY) * 0.5;

				double dx = cx - labelCx;
				double dy = cy - labelCy;

				if (dx < -5)
					continue;

				double score = Math.Abs(dx) + Math.Abs(dy) * 2.0;

				if (score < bestScore)
				{
					bestScore = score;
					best = ent;
				}
			}

			return best;
		}

		private static string Normalize(string text)
		{
			if (text == null)
				return "";

			return text
				.Replace("\r", " ")
				.Replace("\n", " ")
				.Trim();
		}
	}
}