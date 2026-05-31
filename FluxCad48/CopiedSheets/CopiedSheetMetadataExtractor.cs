using FluxCad48.Geometry;
using FluxCad48.ShapeViewAnalysis;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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

			foreach (SheetEntity ent in texts)
			{
				string text = Normalize(ent.Text);

				if (!string.IsNullOrWhiteSpace(text))
					metadata.RawTexts.Add(text);
			}

			// 1) BOM 행 기반으로 재질/수량 동시 추출
			ExtractBomMetadataByMaterialRow(texts, metadata);

			// 2) 부가 수량 정보는 항상 조사
			metadata.ExtraQuantityText = FindSetText(texts);

			// BOM 수량이 없을 때만 부가 수량을 대표 수량으로 사용
			if (string.IsNullOrWhiteSpace(metadata.QuantityText))
			{
				metadata.QuantityText =
					ExtractQuantityFromSetText(metadata.ExtraQuantityText);
			}

			// 3) BOM 방식으로 재질을 못 찾으면 전체 텍스트에서 재질 후보 조사
			if (string.IsNullOrWhiteSpace(metadata.Material))
			{
				metadata.Material = FindMaterialTextAnywhere(texts);
			}

			if (!string.IsNullOrWhiteSpace(metadata.QuantityText))
			{
				if (!string.IsNullOrWhiteSpace(metadata.ExtraQuantityText))
					metadata.QuantityState = QuantityState.FromSet;
				else
					metadata.QuantityState = QuantityState.Exact;
			}
			else
			{
				metadata.QuantityState = QuantityState.Empty;
			}

			int qty;
			if (int.TryParse(metadata.QuantityText, out qty))
				metadata.Quantity = qty;

			record.Metadata = metadata;

			return metadata;
		}

		private static string FindMaterialTextAnywhere(List<SheetEntity> texts)
		{
			foreach (SheetEntity ent in texts)
			{
				if (ent == null)
					continue;

				string s = NormalizeText(ent.Text);

				if (LooksLikeMaterialValue(s))
					return CleanMaterialValue(ent.Text);
			}

			return "";
		}

		private static void ExtractBomMetadataByMaterialRow(
	List<SheetEntity> texts,
	SheetMetadata metadata)
		{
			SheetEntity matHeader = FindMaterialHeader(texts);
			SheetEntity qtyHeader = FindQuantityHeader(texts);

			if (matHeader == null || qtyHeader == null)
				return;

			if (matHeader.Bounds == null || qtyHeader.Bounds == null)
				return;

			SheetEntity matValue =
				FindValueNearHeaderColumn(texts, matHeader, true);

			if (matValue == null)
				return;

			metadata.Material = CleanMaterialValue(matValue.Text);

			SheetEntity qtyValue =
				FindValueAtSameRowAndHeaderColumn(
					texts,
					matValue,
					qtyHeader,
					false);

			if (qtyValue != null)
				metadata.QuantityText = CleanQuantityValue(qtyValue.Text);
		}

		private static SheetEntity FindValueNearHeaderColumn(
	List<SheetEntity> texts,
	SheetEntity header,
	bool material)
		{
			double hx = CenterX(header);
			double hy = CenterY(header);

			SheetEntity best = null;
			double bestScore = double.MaxValue;

			foreach (SheetEntity ent in texts)
			{
				if (ent == null || ent == header || ent.Bounds == null)
					continue;

				string s = NormalizeText(ent.Text);

				if (IsMaterialHeader(s) || IsQuantityHeader(s))
					continue;

				if (material)
				{
					if (!LooksLikeMaterialValue(s))
						continue;
				}
				else
				{
					if (!LooksLikeQuantityValue(s))
						continue;
				}

				double dx = Math.Abs(CenterX(ent) - hx);
				double dy = Math.Abs(CenterY(ent) - hy);

				if (dx > 300.0)
					continue;

				if (dy > 1000.0)
					continue;

				double score = dx * 2.0 + dy;

				if (score < bestScore)
				{
					bestScore = score;
					best = ent;
				}
			}

			return best;
		}

		private static SheetEntity FindValueAtSameRowAndHeaderColumn(
	List<SheetEntity> texts,
	SheetEntity rowAnchor,
	SheetEntity columnHeader,
	bool material)
		{
			double rowY = CenterY(rowAnchor);
			double colX = CenterX(columnHeader);

			SheetEntity best = null;
			double bestScore = double.MaxValue;

			foreach (SheetEntity ent in texts)
			{
				if (ent == null || ent.Bounds == null)
					continue;

				if (ent == rowAnchor || ent == columnHeader)
					continue;

				string s = NormalizeText(ent.Text);

				if (IsMaterialHeader(s) || IsQuantityHeader(s))
					continue;

				if (material)
				{
					if (!LooksLikeMaterialValue(s))
						continue;
				}
				else
				{
					if (!LooksLikeQuantityValue(s))
						continue;
				}

				double dx = Math.Abs(CenterX(ent) - colX);
				double dy = Math.Abs(CenterY(ent) - rowY);

				if (dx > 300.0)
					continue;

				// 같은 BOM 행 조건
				if (dy > 150.0)
					continue;

				double score = dx * 2.0 + dy;

				if (score < bestScore)
				{
					bestScore = score;
					best = ent;
				}
			}

			return best;
		}

		private static double CenterX(SheetEntity ent)
		{
			return (ent.Bounds.MinX + ent.Bounds.MaxX) * 0.5;
		}

		private static double CenterY(SheetEntity ent)
		{
			return (ent.Bounds.MinY + ent.Bounds.MaxY) * 0.5;
		}


		private static string ExtractQuantityFromSetText(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return "";

			Match m = Regex.Match(text, @"^\s*(\d+)\s*[\*xX]");

			if (m.Success)
				return m.Groups[1].Value;

			return "";
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


		private static string ExtractMaterialFromBomColumnBidirectional(
	List<SheetEntity> texts)
		{
			SheetEntity header = FindMaterialHeader(texts);

			if (header == null || header.Bounds == null)
				return "";

			double headerCx = (header.Bounds.MinX + header.Bounds.MaxX) * 0.5;
			double headerCy = (header.Bounds.MinY + header.Bounds.MaxY) * 0.5;

			List<SheetEntity> candidates = new List<SheetEntity>();

			foreach (SheetEntity t in texts)
			{
				if (t == null || t == header || t.Bounds == null)
					continue;

				string s = NormalizeText(t.Text);

				if (IsMaterialHeader(s))
					continue;

				if (!LooksLikeMaterialValue(s))
					continue;

				double cx = (t.Bounds.MinX + t.Bounds.MaxX) * 0.5;
				double cy = (t.Bounds.MinY + t.Bounds.MaxY) * 0.5;

				double dx = Math.Abs(cx - headerCx);
				double dy = Math.Abs(cy - headerCy);

				if (dx > 300.0)
					continue;

				if (dy > 800.0)
					continue;

				candidates.Add(t);
			}

			if (candidates.Count == 0)
				return "";

			candidates.Sort(delegate (SheetEntity a, SheetEntity b)
			{
				double ay = (a.Bounds.MinY + a.Bounds.MaxY) * 0.5;
				double by = (b.Bounds.MinY + b.Bounds.MaxY) * 0.5;

				double da = Math.Abs(ay - headerCy);
				double db = Math.Abs(by - headerCy);

				return da.CompareTo(db);
			});

			return CleanMaterialValue(candidates[0].Text);
		}

		// 당분간 사용하지 말 것. 
		private static string ExtractQuantityFromBomColumnBidirectional(
	List<SheetEntity> texts)
		{
			SheetEntity header = FindQuantityHeader(texts);

			if (header == null || header.Bounds == null)
				return "";

			double headerCx = (header.Bounds.MinX + header.Bounds.MaxX) * 0.5;
			double headerCy = (header.Bounds.MinY + header.Bounds.MaxY) * 0.5;

			List<SheetEntity> candidates = new List<SheetEntity>();

			foreach (SheetEntity t in texts)
			{
				if (t == null || t == header || t.Bounds == null)
					continue;

				string s = NormalizeText(t.Text);

				if (IsQuantityHeader(s))
					continue;

				if (!LooksLikeQuantityValue(s))
					continue;

				double cx = (t.Bounds.MinX + t.Bounds.MaxX) * 0.5;
				double cy = (t.Bounds.MinY + t.Bounds.MaxY) * 0.5;

				double dx = Math.Abs(cx - headerCx);
				double dy = Math.Abs(cy - headerCy);

				if (dx > 300.0)
					continue;

				if (dy > 800.0)
					continue;

				candidates.Add(t);
			}

			if (candidates.Count == 0)
				return "";

			candidates.Sort(delegate (SheetEntity a, SheetEntity b)
			{
				double ay = (a.Bounds.MinY + a.Bounds.MaxY) * 0.5;
				double by = (b.Bounds.MinY + b.Bounds.MaxY) * 0.5;

				double da = Math.Abs(ay - headerCy);
				double db = Math.Abs(by - headerCy);

				return da.CompareTo(db);
			});

			return CleanQuantityValue(candidates[0].Text);
		}


		private static SheetEntity FindMaterialHeader(List<SheetEntity> texts)
		{
			foreach (SheetEntity t in texts)
			{
				if (t == null)
					continue;

				if (IsMaterialHeader(NormalizeText(t.Text)))
					return t;
			}

			return null;
		}

		private static SheetEntity FindQuantityHeader(List<SheetEntity> texts)
		{
			foreach (SheetEntity t in texts)
			{
				if (t == null)
					continue;

				if (IsQuantityHeader(NormalizeText(t.Text)))
					return t;
			}

			return null;
		}

		private static bool IsMaterialHeader(string s)
		{
			if (string.IsNullOrWhiteSpace(s))
				return false;

			string t = s.Trim().ToUpperInvariant();

			t = t.Replace(" ", "");
			t = t.Replace(".", "");
			t = t.Replace(":", "");

			return
				t == "MATL" ||
				t == "MAT'L" ||
				t == "MTL" ||
				t == "MT'L" ||
				t == "MATERIAL" ||
				t == "MATERIALS" ||
				t.Contains("MATERIAL") ||
				t == "재질";
		}

		private static bool IsQuantityHeader(string s)
		{
			return
				s == "QTY" ||
				s == "Q'TY" ||
				s == "QUANTITY" ||
				s == "수량";
		}

		private static string NormalizeText(string text)
		{
			if (text == null)
				return "";

			string s = text.Trim().ToUpperInvariant();

			s = s.Replace(" ", "");
			s = s.Replace(".", "");
			s = s.Replace(":", "");

			return s;
		}

		private static bool LooksLikeMaterialValue(string s)
		{
			if (string.IsNullOrWhiteSpace(s))
				return false;

			string raw = s.Trim();
			string t = NormalizeText(s);

			// 도장/색상/비고 문장은 재질에서 제외
			if (raw.Contains("도장") ||
				raw.Contains("색상") ||
				t.Contains("RAL") ||
				t.Contains("KCC") ||
				t.Contains("EX8816") ||
				t.Contains("일반모따기") ||
				t.Contains("총수량") ||
				t.Contains("대칭"))
				return false;

			if (t.Contains("SS400")) return true;
			if (t.Contains("SS41")) return true;
			if (t.Contains("SUS304")) return true;
			if (t.Contains("SUS316")) return true;
			if (t.StartsWith("SUS")) return true;
			if (t.Contains("SPHC")) return true;
			if (t.Contains("SM45C")) return true;

			// AL은 RAL1018 같은 색상 코드와 충돌하므로 단독 패턴으로만 인정
			if (Regex.IsMatch(t, @"^AL[0-9A-Z\-]*$"))
				return true;

			return false;
		}

		private static bool LooksLikeQuantityValue(string s)
		{
			if (string.IsNullOrWhiteSpace(s))
				return false;

			s = NormalizeText(s);
			s = s.Replace("EA", "");

			// 단순 수량: 1, 2, 12
			if (Regex.IsMatch(s, @"^\d+$"))
				return true;

			// 곱셈 수량: 1*2, 4X2
			if (Regex.IsMatch(s, @"^\d+[\*X]\d+$"))
				return true;

			// 대칭 수량: 1/1(대칭)*2
			if (Regex.IsMatch(s, @"^\d+/\d+\(.+\)[\*X]\d+$"))
				return true;

			// 조금 더 느슨한 현업형: 숫자/슬래시/괄호/한글/곱셈만 포함
			if (Regex.IsMatch(s, @"^[0-9/()\*X가-힣]+$") &&
				(s.Contains("*") || s.Contains("X") || s.Contains("/") || s.Contains("대칭")))
				return true;

			return false;
		}

		private static string CleanMaterialValue(string text)
		{
			if (text == null)
				return "";

			return text.Trim();
		}

		private static string CleanQuantityValue(string text)
		{
			if (text == null)
				return "";

			string s = text.Trim();
			s = s.Replace("EA", "");
			s = s.Replace("ea", "");
			return s.Trim();
		}
	}
}