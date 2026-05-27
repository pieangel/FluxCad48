using Bricscad.EditorInput;
using FluxCad48.Brics;
using FluxCad48.Geometry;
using FluxCad48.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;

namespace FluxCad48.Sheets
{
	public static class SheetFrameDetector
	{
		public static List<SheetFrameCandidate> Detect(
			IReadOnlyList<Entity> selectedEntities,
			Editor ed = null)
		{
			return DetectCore(
				selectedEntities,
				null,
				false,
				ed);
		}

		private static bool IsFrameLikeEntity(Entity ent, Bounds2D bounds)
		{
			if (ent is BlockReference)
				return IsFrameLikeBlock((BlockReference)ent, bounds);

			if (ent is Polyline)
				return IsFrameLikeClosedPolyline((Polyline)ent, bounds);

			if (ent is Polyline2d || ent is Polyline3d)
				return IsFrameLikeByBounds(bounds);

			return false;
		}

		private static bool IsLargeEnoughFrame(Bounds2D b)
		{
			if (b.Width <= 0 || b.Height <= 0)
				return false;

			if (b.Width < 100 || b.Height < 100)
				return false;

			double area = b.Width * b.Height;
			if (area < 10000)
				return false;

			return true;
		}

		private static bool IsFrameLikeBlock(BlockReference br, Bounds2D bounds)
		{
			// 현재 실험상 프레임이 BlockReference로 잘 선택되므로
			// BlockReference는 강한 프레임 후보로 인정합니다.
			return IsFrameLikeByBounds(bounds);
		}

		private static bool IsFrameLikeClosedPolyline(Polyline pline, Bounds2D bounds)
		{
			if (!pline.Closed)
				return false;

			if (pline.NumberOfVertices >= 4)
				return IsFrameLikeByBounds(bounds);

			return false;
		}

		private static bool IsFrameLikeByBounds(Bounds2D bounds)
		{
			double ratio = bounds.Width / bounds.Height;

			if (ratio < 0.2 || ratio > 20.0)
				return false;

			return true;
		}

		private static int CountEntitiesInsideBounds(
			IReadOnlyList<Entity> allSelected,
			Entity frameEntity,
			Bounds2D frameBounds)
		{
			int count = 0;

			foreach (var ent in allSelected)
			{
				if (ent.ObjectId == frameEntity.ObjectId)
					continue;

				var b = BricscadEntityTools.GetEntityBounds(ent);
				if (b == null)
					continue;

				if (ContainsCenter(frameBounds, b))
					count++;
			}

			return count;
		}

		private static bool ContainsCenter(Bounds2D outer, Bounds2D inner)
		{
			double cx = (inner.MinX + inner.MaxX) * 0.5;
			double cy = (inner.MinY + inner.MaxY) * 0.5;

			return cx >= outer.MinX && cx <= outer.MaxX &&
				   cy >= outer.MinY && cy <= outer.MaxY;
		}

		private static double CalculateFrameScore(
			Entity ent,
			Bounds2D bounds,
			int insideCount)
		{
			double score = 0;

			if (ent is BlockReference)
				score += 50;

			if (ent is Polyline)
				score += 40;

			score += Math.Min(insideCount, 100) * 0.5;

			double area = bounds.Width * bounds.Height;
			score += Math.Min(area / 100000.0, 30);

			return score;
		}

		private static List<SheetFrameCandidate> RemoveDuplicateFrames(
			List<SheetFrameCandidate> frames)
		{
			var result = new List<SheetFrameCandidate>();

			foreach (var frame in frames.OrderByDescending(f => f.Score))
			{
				bool duplicated = result.Any(existing =>
					IsAlmostSameBounds(existing.Bounds, frame.Bounds));

				if (!duplicated)
					result.Add(frame);
			}

			return result;
		}

		private static bool IsAlmostSameBounds(Bounds2D a, Bounds2D b)
		{
			double tol = 5.0;

			return Math.Abs(a.MinX - b.MinX) < tol &&
				   Math.Abs(a.MinY - b.MinY) < tol &&
				   Math.Abs(a.MaxX - b.MaxX) < tol &&
				   Math.Abs(a.MaxY - b.MaxY) < tol;
		}

		private static List<SheetFrameCandidate> ResolveOverlappingFrameCandidates(
			List<SheetFrameCandidate> candidates)
		{
			var result = new List<SheetFrameCandidate>();

			for (int i = 0; i < candidates.Count; i++)
			{
				SheetFrameCandidate child = candidates[i];

				bool nested = false;
				string parentHandle = "";

				for (int j = 0; j < candidates.Count; j++)
				{
					if (i == j)
						continue;

					SheetFrameCandidate parent = candidates[j];

					if (child.Bounds == null || parent.Bounds == null)
						continue;

					if (parent.Bounds.Area <= child.Bounds.Area)
						continue;

					double ratio = child.Bounds.ContainedRatioIn(parent.Bounds);

					if (ratio >= 0.90)
					{
						nested = true;
						parentHandle = parent.Handle;
						break;
					}
				}

				child.IsNestedCandidate = nested;
				child.ParentHandle = parentHandle;

				if (!nested)
					result.Add(child);
			}

			return result;
		}

		public static List<SheetFrameCandidate> Detect(
			IReadOnlyList<Entity> selectedEntities,
			Bounds2D pickBounds,
			Editor ed = null)
		{
			return DetectCore(
				selectedEntities,
				pickBounds,
				true,
				ed);
		}

		private static List<SheetFrameCandidate> DetectCore(
			IReadOnlyList<Entity> selectedEntities,
			Bounds2D pickBounds,
			bool hasPickBounds,
			Editor ed)
		{
			var candidates = new List<SheetFrameCandidate>();

			foreach (var ent in selectedEntities)
			{
				var bounds = BricscadEntityTools.GetEntityBounds(ent);
				if (bounds == null)
					continue;

				if (!IsLargeEnoughFrame(bounds))
					continue;

				if (!IsFrameLikeEntity(ent, bounds))
					continue;

				int insideCount = CountEntitiesInsideBounds(
					selectedEntities,
					ent,
					bounds);

				if (insideCount < 5)
					continue;

				double score = CalculateFrameScore(ent, bounds, insideCount);

				candidates.Add(new SheetFrameCandidate
				{
					ObjectId = ent.ObjectId,
					Handle = ent.Handle.ToString(),
					EntityType = ent.GetType().Name,
					Layer = ent.Layer,
					Bounds = bounds,
					InsideEntityCount = insideCount,
					Score = score
				});
			}

			List<SheetFrameCandidate> lineFrames =
				DetectFourLineRectangleFrames(selectedEntities, ed);

			candidates.AddRange(lineFrames);

			ed?.WriteMessage(
				"\n[FrameDetect] FourLineCandidates=" +
				lineFrames.Count);

			ed?.WriteMessage($"\n[FrameDetect] RawCandidates={candidates.Count}");

			candidates = RemoveDuplicateFrames(candidates);

			ed?.WriteMessage($"\n[FrameDetect] AfterDuplicateFilter={candidates.Count}");

			var filtered = ResolveOverlappingFrameCandidates(candidates);

			ed?.WriteMessage($"\n[FrameDetect] AfterOverlapFilter={filtered.Count}");

			if (hasPickBounds)
			{
				filtered = RemovePartialSelectionFrames(
					filtered,
					pickBounds,
					selectedEntities,
					ed);

				ed?.WriteMessage($"\n[FrameDetect] AfterPartialFilter={filtered.Count}");
			}

			return filtered
				.OrderBy(f => f.Bounds.MinY)
				.ThenBy(f => f.Bounds.MinX)
				.ToList();
		}

		private sealed class FrameLineInfo
		{
			public Entity Entity { get; set; }
			public ObjectId ObjectId { get; set; }
			public string Handle { get; set; }

			public bool IsHorizontal { get; set; }
			public bool IsVertical { get; set; }

			public double X1 { get; set; }
			public double X2 { get; set; }
			public double Y1 { get; set; }
			public double Y2 { get; set; }

			public double Length
			{
				get
				{
					if (IsHorizontal)
						return X2 - X1;

					return Y2 - Y1;
				}
			}

			public FrameLineInfo()
			{
				Handle = "";
			}
		}

		private static List<SheetFrameCandidate> DetectFourLineRectangleFrames(
			IReadOnlyList<Entity> selectedEntities,
			Editor ed = null)
		{
			var result = new List<SheetFrameCandidate>();

			List<FrameLineInfo> lines =
				CollectHorizontalVerticalLines(selectedEntities);

			List<FrameLineInfo> horizontals =
				lines.Where(x => x.IsHorizontal).ToList();

			List<FrameLineInfo> verticals =
				lines.Where(x => x.IsVertical).ToList();

			double tol = 5.0;

			foreach (FrameLineInfo h1 in horizontals)
			{
				foreach (FrameLineInfo h2 in horizontals)
				{
					if (h1 == h2)
						continue;

					double topY = System.Math.Max(h1.Y1, h2.Y1);
					double bottomY = System.Math.Min(h1.Y1, h2.Y1);

					if (topY - bottomY < 100.0)
						continue;

					FrameLineInfo top = h1.Y1 >= h2.Y1 ? h1 : h2;
					FrameLineInfo bottom = h1.Y1 < h2.Y1 ? h1 : h2;

					foreach (FrameLineInfo v1 in verticals)
					{
						foreach (FrameLineInfo v2 in verticals)
						{
							if (v1 == v2)
								continue;

							double leftX = System.Math.Min(v1.X1, v2.X1);
							double rightX = System.Math.Max(v1.X1, v2.X1);

							if (rightX - leftX < 100.0)
								continue;

							FrameLineInfo left = v1.X1 <= v2.X1 ? v1 : v2;
							FrameLineInfo right = v1.X1 > v2.X1 ? v1 : v2;

							if (!IsRectangleCornerMatched(
								top,
								bottom,
								left,
								right,
								tol))
							{
								continue;
							}

							Bounds2D bounds =
								new Bounds2D(
									leftX,
									bottomY,
									rightX,
									topY);

							if (!IsLargeEnoughFrame(bounds))
								continue;

							if (!IsFrameLikeByBounds(bounds))
								continue;

							int insideCount =
								CountEntitiesInsideBounds(
									selectedEntities,
									bounds);

							if (insideCount < 5)
								continue;

							double area = bounds.Width * bounds.Height;
							double score =
								35.0 +
								System.Math.Min(insideCount, 100) * 0.5 +
								System.Math.Min(area / 100000.0, 30);

							SheetFrameCandidate candidate =
								new SheetFrameCandidate();

							candidate.ObjectId = top.ObjectId;
							candidate.Handle =
								top.Handle + "+" +
								bottom.Handle + "+" +
								left.Handle + "+" +
								right.Handle;

							candidate.EntityType = "FourLineRectangle";
							candidate.Layer = top.Entity.Layer;
							candidate.Bounds = bounds;
							candidate.InsideEntityCount = insideCount;
							candidate.Score = score;

							result.Add(candidate);
						}
					}
				}
			}

			return result;
		}

		private static List<FrameLineInfo> CollectHorizontalVerticalLines(
			IReadOnlyList<Entity> selectedEntities)
		{
			var result = new List<FrameLineInfo>();

			double angleTol = 1.0;
			double minLength = 100.0;

			foreach (Entity ent in selectedEntities)
			{
				Line line = ent as Line;

				if (line == null)
					continue;

				double x1 = line.StartPoint.X;
				double y1 = line.StartPoint.Y;
				double x2 = line.EndPoint.X;
				double y2 = line.EndPoint.Y;

				double dx = System.Math.Abs(x2 - x1);
				double dy = System.Math.Abs(y2 - y1);

				if (dx < minLength && dy < minLength)
					continue;

				if (dy <= angleTol && dx >= minLength)
				{
					FrameLineInfo info = new FrameLineInfo();
					info.Entity = ent;
					info.ObjectId = ent.ObjectId;
					info.Handle = ent.Handle.ToString();
					info.IsHorizontal = true;
					info.X1 = System.Math.Min(x1, x2);
					info.X2 = System.Math.Max(x1, x2);
					info.Y1 = (y1 + y2) * 0.5;
					info.Y2 = info.Y1;

					result.Add(info);
				}
				else if (dx <= angleTol && dy >= minLength)
				{
					FrameLineInfo info = new FrameLineInfo();
					info.Entity = ent;
					info.ObjectId = ent.ObjectId;
					info.Handle = ent.Handle.ToString();
					info.IsVertical = true;
					info.X1 = (x1 + x2) * 0.5;
					info.X2 = info.X1;
					info.Y1 = System.Math.Min(y1, y2);
					info.Y2 = System.Math.Max(y1, y2);

					result.Add(info);
				}
			}

			return result;
		}

		private static bool IsRectangleCornerMatched(
			FrameLineInfo top,
			FrameLineInfo bottom,
			FrameLineInfo left,
			FrameLineInfo right,
			double tol)
		{
			if (!HorizontalCoversX(top, left.X1, tol))
				return false;

			if (!HorizontalCoversX(top, right.X1, tol))
				return false;

			if (!HorizontalCoversX(bottom, left.X1, tol))
				return false;

			if (!HorizontalCoversX(bottom, right.X1, tol))
				return false;

			if (!VerticalCoversY(left, top.Y1, tol))
				return false;

			if (!VerticalCoversY(left, bottom.Y1, tol))
				return false;

			if (!VerticalCoversY(right, top.Y1, tol))
				return false;

			if (!VerticalCoversY(right, bottom.Y1, tol))
				return false;

			return true;
		}

		private static bool HorizontalCoversX(
			FrameLineInfo h,
			double x,
			double tol)
		{
			return x >= h.X1 - tol && x <= h.X2 + tol;
		}

		private static bool VerticalCoversY(
			FrameLineInfo v,
			double y,
			double tol)
		{
			return y >= v.Y1 - tol && y <= v.Y2 + tol;
		}

		private static int CountEntitiesInsideBounds(
			IReadOnlyList<Entity> allSelected,
			Bounds2D frameBounds)
		{
			int count = 0;

			foreach (Entity ent in allSelected)
			{
				Bounds2D b =
					BricscadEntityTools.GetEntityBounds(ent);

				if (b == null)
					continue;

				if (ContainsCenter(frameBounds, b))
					count++;
			}

			return count;
		}

		private static List<SheetFrameCandidate> RemovePartialSelectionFrames(
			List<SheetFrameCandidate> frames,
			Bounds2D pickBounds,
			IReadOnlyList<Entity> selectedEntities,
			Editor ed = null)
		{
			var result = new List<SheetFrameCandidate>();

			foreach (var frame in frames)
			{
				double containedRatio =
					frame.Bounds.ContainedRatioIn(pickBounds);

				bool partial = containedRatio < 0.98;

				ed?.WriteMessage(
					$"\n[PartialCheck] Handle={frame.Handle}, " +
					$"ContainedRatio={containedRatio:0.000}, " +
					$"Partial={partial}");

				if (!partial)
					result.Add(frame);
			}

			return result;
		}

		public static List<SheetFrameCandidate> DetectContainingFrames(
	IReadOnlyList<Entity> searchEntities,
	Bounds2D selectedBounds,
	Editor ed = null)
		{
			List<SheetFrameCandidate> frames =
				DetectCore(searchEntities, null, false, ed);

			var result = new List<SheetFrameCandidate>();

			foreach (SheetFrameCandidate frame in frames)
			{
				if (IsBoundsInsideFrame(selectedBounds, frame.Bounds))
					result.Add(frame);
			}

			ed?.WriteMessage(
				"\n[FrameDetect] ContainingFrames=" + result.Count);

			return result
				.OrderBy(f => f.Bounds.Area)
				.ThenByDescending(f => f.Score)
				.ToList();
		}

		private static bool IsBoundsInsideFrame(
			Bounds2D inner,
			Bounds2D frame)
		{
			if (inner == null || !inner.IsValid)
				return false;

			if (frame == null || !frame.IsValid)
				return false;

			double frameMinSize = Math.Min(frame.Width, frame.Height);
			double tol = frameMinSize * 0.003;

			if (tol < 1.0)
				tol = 1.0;

			if (tol > 8.0)
				tol = 8.0;

			return
				inner.MinX >= frame.MinX - tol &&
				inner.MaxX <= frame.MaxX + tol &&
				inner.MinY >= frame.MinY - tol &&
				inner.MaxY <= frame.MaxY + tol;
		}
	}
}