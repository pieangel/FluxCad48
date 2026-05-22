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

			ed?.WriteMessage($"\n[FrameDetect] RawCandidates={candidates.Count}");

			candidates = RemoveDuplicateFrames(candidates);

			ed?.WriteMessage($"\n[FrameDetect] AfterDuplicateFilter={candidates.Count}");

			var filtered = ResolveOverlappingFrameCandidates(candidates);

			ed?.WriteMessage($"\n[FrameDetect] AfterOverlapFilter={filtered.Count}");

			return filtered
				.OrderBy(f => f.Bounds.MinY)
				.ThenBy(f => f.Bounds.MinX)
				.ToList();
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
	}
}