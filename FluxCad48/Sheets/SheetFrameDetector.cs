using Bricscad.EditorInput;
using FluxCad48.Brics;
using FluxCad48.Geometry;
using FluxCad48.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Colors;

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
				ed,
				true); // 기존 방식: 중첩 프레임 제거
		}

		private static bool IsFrameLikeEntity(Entity ent, Bounds2D bounds)
		{
			if (ent is BlockReference)
				return false; // BlockReference 자체는 프레임 후보 금지

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
			return DetectCore(selectedEntities, pickBounds, true, ed, true);
		}

		private static List<SheetFrameCandidate> DetectCore(
			IReadOnlyList<Entity> selectedEntities,
			Bounds2D pickBounds,
			bool hasPickBounds,
			Editor ed,
			bool resolveNestedFrames)
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

			// 여기를 추가
			List<SheetFrameCandidate> blockInnerFrames =
				DetectFramesInsideBlockReferences(selectedEntities, ed);

			candidates.AddRange(blockInnerFrames);

			ed?.WriteMessage(
				"\n[FrameDetect] BlockInnerFrameCandidates=" +
				blockInnerFrames.Count);

			ed?.WriteMessage($"\n[FrameDetect] RawCandidates={candidates.Count}");

			Bounds2D selectedBoundsForReject =
				CalculateEntitiesBounds(selectedEntities);

			candidates =
				RejectSuspiciousLargeBlockFrames(
					candidates,
					selectedBoundsForReject,
					ed);

			ed?.WriteMessage(
				$"\n[FrameDetect] AfterSuspiciousLargeBlockFilter={candidates.Count}");


			foreach (SheetFrameCandidate c in candidates)
			{
				ed?.WriteMessage(
					"\n[RawFrameCandidate] Handle=" + c.Handle +
					", Type=" + c.EntityType +
					", Bounds=" + c.Bounds +
					", Score=" + c.Score +
					", InsideCount=" + c.InsideEntityCount);
			}

			List<SheetFrameCandidate> filtered;

			if (resolveNestedFrames)
			{
				filtered = ResolveOverlappingFrameCandidates(candidates);
				ed?.WriteMessage($"\n[FrameDetect] AfterOverlapFilter={filtered.Count}");

				filtered = RemoveDuplicateFramesByBounds(filtered);

				ed?.WriteMessage(
					"\n[FrameDetect] AfterDuplicateBoundsFilter=" +
					filtered.Count);
			}
			else
			{
				filtered = candidates;
				ed?.WriteMessage($"\n[FrameDetect] SkipOverlapFilter={filtered.Count}");
			}

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

		private static List<SheetFrameCandidate> RemoveDuplicateFramesByBounds(
	List<SheetFrameCandidate> frames)
		{
			var result = new List<SheetFrameCandidate>();

			foreach (SheetFrameCandidate frame in frames.OrderByDescending(f => f.Score))
			{
				bool duplicated = result.Any(existing =>
					IsAlmostSameBounds(existing.Bounds, frame.Bounds));

				if (!duplicated)
					result.Add(frame);
			}

			return result;
		}

		private static List<SheetFrameCandidate> DetectFramesInsideBlockReferences(
	IReadOnlyList<Entity> selectedEntities,
	Editor ed = null)
		{
			var result = new List<SheetFrameCandidate>();

			if (selectedEntities == null)
				return result;

			foreach (Entity ent in selectedEntities)
			{
				BlockReference br = ent as BlockReference;

				if (br == null)
					continue;

				List<WorldLineInfo> worldLines = new List<WorldLineInfo>();

				CollectWorldLinesFromBlockReference(
					br,
					br.BlockTransform,
					worldLines,
					0);

				List<SheetFrameCandidate> frames =
					DetectFourLineRectangleFramesFromWorldLines(
						worldLines,
						br,
						ed);

				result.AddRange(frames);
			}

			return result;
		}

		private sealed class WorldLineInfo
		{
			public string Handle { get; set; }
			public ObjectId SourceObjectId { get; set; }
			public string Layer { get; set; }

			public Point3d StartPoint { get; set; }
			public Point3d EndPoint { get; set; }

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
		}

		private static void CollectWorldLinesFromBlockReference(
	BlockReference br,
	Matrix3d transform,
	List<WorldLineInfo> result,
	int depth)
		{
			if (br == null)
				return;

			if (depth > 20)
				return;

			Transaction tr =
				br.Database.TransactionManager.TopTransaction;

			if (tr == null)
				return;

			BlockTableRecord btr =
				tr.GetObject(
					br.BlockTableRecord,
					OpenMode.ForRead) as BlockTableRecord;

			if (btr == null)
				return;

			foreach (ObjectId childId in btr)
			{
				Entity child =
					tr.GetObject(childId, OpenMode.ForRead) as Entity;

				if (child == null)
					continue;

				Line line = child as Line;

				if (line != null)
				{
					AddWorldLine(line, transform, result);
					continue;
				}

				Polyline pline = child as Polyline;

				if (pline != null)
				{
					AddWorldPolylineEdges(pline, transform, result);
					continue;
				}

				BlockReference childBr = child as BlockReference;

				if (childBr != null)
				{
					Matrix3d childTransform =
						childBr.BlockTransform * transform;

					CollectWorldLinesFromBlockReference(
						childBr,
						childTransform,
						result,
						depth + 1);
				}
			}
		}

		private static void AddWorldLine(
	Line line,
	Matrix3d transform,
	List<WorldLineInfo> result)
		{
			Point3d p1 = line.StartPoint.TransformBy(transform);
			Point3d p2 = line.EndPoint.TransformBy(transform);

			AddWorldLineCore(
				line.ObjectId,
				line.Handle.ToString(),
				line.Layer,
				p1,
				p2,
				result);
		}

		private static void AddWorldPolylineEdges(
	Polyline pline,
	Matrix3d transform,
	List<WorldLineInfo> result)
		{
			int n = pline.NumberOfVertices;

			if (n < 2)
				return;

			int edgeCount = pline.Closed ? n : n - 1;

			for (int i = 0; i < edgeCount; i++)
			{
				int j = (i + 1) % n;

				Point2d p2a = pline.GetPoint2dAt(i);
				Point2d p2b = pline.GetPoint2dAt(j);

				Point3d p1 =
					new Point3d(p2a.X, p2a.Y, pline.Elevation)
					.TransformBy(transform);

				Point3d p2 =
					new Point3d(p2b.X, p2b.Y, pline.Elevation)
					.TransformBy(transform);

				AddWorldLineCore(
					pline.ObjectId,
					pline.Handle.ToString(),
					pline.Layer,
					p1,
					p2,
					result);
			}
		}

		private static void AddWorldLineCore(
	ObjectId sourceObjectId,
	string handle,
	string layer,
	Point3d p1,
	Point3d p2,
	List<WorldLineInfo> result)
		{
			double angleTol = 1.0;
			double minLength = 100.0;

			double dx = Math.Abs(p2.X - p1.X);
			double dy = Math.Abs(p2.Y - p1.Y);

			if (dx < minLength && dy < minLength)
				return;

			WorldLineInfo info = new WorldLineInfo();
			info.SourceObjectId = sourceObjectId;
			info.Handle = handle;
			info.Layer = layer;
			info.StartPoint = p1;
			info.EndPoint = p2;

			if (dy <= angleTol && dx >= minLength)
			{
				info.IsHorizontal = true;
				info.X1 = Math.Min(p1.X, p2.X);
				info.X2 = Math.Max(p1.X, p2.X);
				info.Y1 = (p1.Y + p2.Y) * 0.5;
				info.Y2 = info.Y1;

				result.Add(info);
			}
			else if (dx <= angleTol && dy >= minLength)
			{
				info.IsVertical = true;
				info.X1 = (p1.X + p2.X) * 0.5;
				info.X2 = info.X1;
				info.Y1 = Math.Min(p1.Y, p2.Y);
				info.Y2 = Math.Max(p1.Y, p2.Y);

				result.Add(info);
			}
		}

		private static List<SheetFrameCandidate> DetectFourLineRectangleFramesFromWorldLines(
	List<WorldLineInfo> lines,
	BlockReference ownerBlock,
	Editor ed = null)
		{
			var result = new List<SheetFrameCandidate>();

			if (lines == null || lines.Count == 0)
				return result;

			List<WorldLineInfo> horizontals =
				lines.Where(x => x.IsHorizontal).ToList();

			List<WorldLineInfo> verticals =
				lines.Where(x => x.IsVertical).ToList();

			double tol = 5.0;

			foreach (WorldLineInfo h1 in horizontals)
			{
				foreach (WorldLineInfo h2 in horizontals)
				{
					if (h1 == h2)
						continue;

					double topY = Math.Max(h1.Y1, h2.Y1);
					double bottomY = Math.Min(h1.Y1, h2.Y1);

					if (topY - bottomY < 100.0)
						continue;

					WorldLineInfo top = h1.Y1 >= h2.Y1 ? h1 : h2;
					WorldLineInfo bottom = h1.Y1 < h2.Y1 ? h1 : h2;

					foreach (WorldLineInfo v1 in verticals)
					{
						foreach (WorldLineInfo v2 in verticals)
						{
							if (v1 == v2)
								continue;

							double leftX = Math.Min(v1.X1, v2.X1);
							double rightX = Math.Max(v1.X1, v2.X1);

							if (rightX - leftX < 100.0)
								continue;

							WorldLineInfo left = v1.X1 <= v2.X1 ? v1 : v2;
							WorldLineInfo right = v1.X1 > v2.X1 ? v1 : v2;

							if (!IsWorldRectangleCornerMatched(
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

							double area = bounds.Width * bounds.Height;

							SheetFrameCandidate candidate =
								new SheetFrameCandidate();

							candidate.ObjectId = ownerBlock.ObjectId;
							candidate.Handle =
								"BR:" + ownerBlock.Handle.ToString() +
								"/INNER:" +
								top.Handle + "+" +
								bottom.Handle + "+" +
								left.Handle + "+" +
								right.Handle;

							candidate.EntityType = "BlockInnerFourLineRectangle";
							candidate.Layer = ownerBlock.Layer;
							candidate.Bounds = bounds;
							candidate.InsideEntityCount = 999;
							candidate.Score =
								80.0 + Math.Min(area / 100000.0, 30);

							result.Add(candidate);
						}
					}
				}
			}

			return result;
		}

		private static bool IsWorldRectangleCornerMatched(
	WorldLineInfo top,
	WorldLineInfo bottom,
	WorldLineInfo left,
	WorldLineInfo right,
	double tol)
		{
			if (!WorldHorizontalCoversX(top, left.X1, tol))
				return false;

			if (!WorldHorizontalCoversX(top, right.X1, tol))
				return false;

			if (!WorldHorizontalCoversX(bottom, left.X1, tol))
				return false;

			if (!WorldHorizontalCoversX(bottom, right.X1, tol))
				return false;

			if (!WorldVerticalCoversY(left, top.Y1, tol))
				return false;

			if (!WorldVerticalCoversY(left, bottom.Y1, tol))
				return false;

			if (!WorldVerticalCoversY(right, top.Y1, tol))
				return false;

			if (!WorldVerticalCoversY(right, bottom.Y1, tol))
				return false;

			return true;
		}

		private static bool WorldHorizontalCoversX(
	WorldLineInfo h,
	double x,
	double tol)
		{
			return x >= h.X1 - tol && x <= h.X2 + tol;
		}

		private static bool WorldVerticalCoversY(
	WorldLineInfo v,
	double y,
	double tol)
		{
			return y >= v.Y1 - tol && y <= v.Y2 + tol;
		}

		private static List<SheetFrameCandidate> RejectSuspiciousLargeBlockFrames(
	List<SheetFrameCandidate> candidates,
	Bounds2D selectedBounds,
	Editor ed = null)
		{
			var result = new List<SheetFrameCandidate>();

			if (candidates == null)
				return result;

			foreach (SheetFrameCandidate c in candidates)
			{
				if (c == null || c.Bounds == null || !c.Bounds.IsValid)
					continue;

				bool reject = IsSuspiciousLargeBlockFrame(
					c,
					selectedBounds);

				if (reject)
				{
					ed?.WriteMessage(
						"\n[FrameReject SuspiciousLargeBlock] Handle=" +
						c.Handle +
						", Type=" + c.EntityType +
						", Bounds=" + c.Bounds);

					continue;
				}

				result.Add(c);
			}

			return result;
		}

		private static bool IsSuspiciousLargeBlockFrame(
	SheetFrameCandidate candidate,
	Bounds2D selectedBounds)
		{
			if (candidate == null)
				return true;

			if (candidate.Bounds == null || !candidate.Bounds.IsValid)
				return true;

			if (selectedBounds == null || !selectedBounds.IsValid)
				return false;

			if (!string.Equals(
				candidate.EntityType,
				"BlockReference",
				StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			double areaRatio =
				candidate.Bounds.Area / selectedBounds.Area;

			if (areaRatio < 1.8)
				return false;

			bool hasSimilarFourLine =
				HasSimilarOrSmallerFourLineCandidate(
					candidate,
					selectedBounds);

			if (hasSimilarFourLine)
				return true;

			if (areaRatio > 2.5)
				return true;

			return false;
		}

		private static bool HasSimilarOrSmallerFourLineCandidate(
	SheetFrameCandidate blockCandidate,
	Bounds2D selectedBounds)
		{
			// 일단 단순 버전:
			// selectedBounds보다 훨씬 큰 BlockReference는 위험 후보로 본다.
			// 추후 candidates 전체를 넘겨 FourLineRectangle과 직접 비교하도록 개선 가능.
			if (blockCandidate == null ||
				blockCandidate.Bounds == null ||
				!blockCandidate.Bounds.IsValid)
				return false;

			double areaRatio =
				blockCandidate.Bounds.Area / selectedBounds.Area;

			return areaRatio > 1.8;
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
				DetectCore(searchEntities, null, false, ed, false);

			var result = new List<SheetFrameCandidate>();

			foreach (SheetFrameCandidate frame in frames)
			{
				double centerRatio =
					GetEntityCenterInsideRatio(
						searchEntities,
						frame.Bounds);

				bool accepted = centerRatio >= 0.70;

				ed?.WriteMessage(
					"\n[ContainingCheck] Handle=" + frame.Handle +
					", Type=" + frame.EntityType +
					", CenterRatio=" + centerRatio.ToString("0.000") +
					", Accepted=" + accepted +
					", FrameBounds=" + frame.Bounds +
					", SelectedBounds=" + selectedBounds);

				if (accepted)
					result.Add(frame);
				else
					LogEntitiesOutsideFrame(
						searchEntities,
						frame.Bounds,
						ed);
			}

			ed?.WriteMessage(
				"\n[FrameDetect] ContainingFrames=" + result.Count);

			return result
				.OrderBy(f => f.Bounds.Area)
				.ThenByDescending(f => f.Score)
				.ToList();
		}

		private static double GetEntityCenterInsideRatio(
			IReadOnlyList<Entity> entities,
			Bounds2D frameBounds)
		{
			int total = 0;
			int inside = 0;

			foreach (Entity ent in entities)
			{
				Bounds2D b =
					BricscadEntityTools.GetEntityBounds(ent);

				if (b == null || !b.IsValid)
					continue;

				total++;

				if (ContainsCenter(frameBounds, b))
					inside++;
			}

			if (total == 0)
				return 0.0;

			return (double)inside / (double)total;
		}

		private static void LogEntitiesOutsideFrame(
			IReadOnlyList<Entity> entities,
			Bounds2D frameBounds,
			Editor ed)
		{
			double tol = 5.0;

			foreach (Entity ent in entities)
			{
				Bounds2D b = BricscadEntityTools.GetEntityBounds(ent);

				if (b == null || !b.IsValid)
					continue;

				bool outside =
					b.MinX < frameBounds.MinX - tol ||
					b.MaxX > frameBounds.MaxX + tol ||
					b.MinY < frameBounds.MinY - tol ||
					b.MaxY > frameBounds.MaxY + tol;

				if (!outside)
					continue;

				ed?.WriteMessage(
					"\n[OutsideFrameEntity] Handle=" + ent.Handle +
					", Type=" + ent.GetType().Name +
					", Layer=" + ent.Layer +
					", Bounds=" + b);
			}
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

		public static SheetFrameCandidate FindOwningFrameWithExpansion(
			IReadOnlyList<Entity> selectedEntities,
			IReadOnlyList<Entity> modelSpaceEntities,
			Editor ed = null)
		{
			if (selectedEntities == null || selectedEntities.Count == 0)
				return null;

			Bounds2D selectedBounds = CalculateEntitiesBounds(selectedEntities);

			if (selectedBounds == null || !selectedBounds.IsValid)
				return null;

			ed?.WriteMessage(
				"\n[OwningFrame] SelectedBounds=" + selectedBounds);

			// 1차: 선택된 개체 안에서 직접 프레임 탐색
			List<SheetFrameCandidate> directFrames =
				DetectContainingFrames(
					selectedEntities,
					selectedBounds,
					ed);

			SheetFrameCandidate directBest =
				SelectBestContainingFrame(
					directFrames,
					selectedBounds,
					ed);

			if (directBest != null)
			{
				ed?.WriteMessage(
					"\n[OwningFrame] Found in selectedEntities. Handle=" +
					directBest.Handle);

				return directBest;
			}

			// 2차: 선택 Bounds 주변을 단계적으로 확장하면서 탐색
			double[] expandRatios = new double[]
			{
				1.2,
				1.5,
				2.0,
				3.0
			};

			foreach (double ratio in expandRatios)
			{
				Bounds2D searchBounds =
					ExpandBoundsByRatio(selectedBounds, ratio);

				List<Entity> searchEntities =
					CollectEntitiesIntersectingBounds(
						modelSpaceEntities,
						searchBounds);

				ed?.WriteMessage(
					"\n[OwningFrame] ExpansionRatio=" + ratio.ToString("0.0") +
					", SearchBounds=" + searchBounds +
					", SearchEntities=" + searchEntities.Count);

				if (searchEntities.Count == 0)
					continue;

				List<SheetFrameCandidate> frames =
					DetectContainingFrames(
						searchEntities,
						selectedBounds,
						ed);

				SheetFrameCandidate best =
					SelectBestContainingFrame(
						frames,
						selectedBounds,
						ed);

				if (best != null)
				{
					ed?.WriteMessage(
						"\n[OwningFrame] Found by expansion. Ratio=" +
						ratio.ToString("0.0") +
						", Handle=" + best.Handle);

					return best;
				}
			}

			ed?.WriteMessage("\n[OwningFrame] Not found.");

			return null;
		}

		private static Bounds2D CalculateEntitiesBounds(
			IReadOnlyList<Entity> entities)
		{
			Bounds2D result = null;

			foreach (Entity ent in entities)
			{
				Bounds2D b = BricscadEntityTools.GetEntityBounds(ent);

				if (b == null || !b.IsValid)
					continue;

				if (result == null)
				{
					result = new Bounds2D(
						b.MinX,
						b.MinY,
						b.MaxX,
						b.MaxY);
				}
				else
				{
					result = new Bounds2D(
						Math.Min(result.MinX, b.MinX),
						Math.Min(result.MinY, b.MinY),
						Math.Max(result.MaxX, b.MaxX),
						Math.Max(result.MaxY, b.MaxY));
				}
			}

			return result;
		}

		private static Bounds2D ExpandBoundsByRatio(
			Bounds2D bounds,
			double ratio)
		{
			double cx = (bounds.MinX + bounds.MaxX) * 0.5;
			double cy = (bounds.MinY + bounds.MaxY) * 0.5;

			double halfW = bounds.Width * ratio * 0.5;
			double halfH = bounds.Height * ratio * 0.5;

			return new Bounds2D(
				cx - halfW,
				cy - halfH,
				cx + halfW,
				cy + halfH);
		}

		private static List<Entity> CollectEntitiesIntersectingBounds(
			IReadOnlyList<Entity> entities,
			Bounds2D searchBounds)
		{
			var result = new List<Entity>();

			foreach (Entity ent in entities)
			{
				Bounds2D b = BricscadEntityTools.GetEntityBounds(ent);

				if (b == null || !b.IsValid)
					continue;

				if (Intersects(searchBounds, b))
					result.Add(ent);
			}

			return result;
		}

		private static bool Intersects(
			Bounds2D a,
			Bounds2D b)
		{
			if (a.MaxX < b.MinX)
				return false;

			if (a.MinX > b.MaxX)
				return false;

			if (a.MaxY < b.MinY)
				return false;

			if (a.MinY > b.MaxY)
				return false;

			return true;
		}

		private static SheetFrameCandidate SelectBestContainingFrame(
			List<SheetFrameCandidate> frames,
			Bounds2D selectedBounds,
			Editor ed = null)
		{
			if (frames == null || frames.Count == 0)
				return null;

			var valid = new List<SheetFrameCandidate>();

			foreach (SheetFrameCandidate frame in frames)
			{
				if (frame.Bounds == null || !frame.Bounds.IsValid)
					continue;

				double areaRatio =
					frame.Bounds.Area / selectedBounds.Area;

				if (areaRatio > 100.0)
				{
					ed?.WriteMessage(
						"\n[OwningFrame] RejectTooLargeFrame Handle=" +
						frame.Handle +
						", AreaRatio=" +
						areaRatio.ToString("0.0"));

					continue;
				}

				bool contains =
					IsBoundsInsideFrame(
						selectedBounds,
						frame.Bounds);

				if (!contains)
					continue;

				valid.Add(frame);
			}

			ed?.WriteMessage(
				"\n[OwningFrame] ValidContainingFrames=" + valid.Count);

			if (valid.Count == 0)
				return null;

			return valid
				.OrderBy(f => f.Bounds.Area)
				.ThenByDescending(f => f.Score)
				.FirstOrDefault();
		}

		public static void DebugDrawBoundsForCoordinateCheck(
			Transaction tr,
	Database db,
	Editor ed,
	IReadOnlyList<Entity> selectedEntities)
		{
			if (db == null || selectedEntities == null)
				return;

			List<SheetFrameCandidate> frames =
				DetectCore(
					selectedEntities,
					null,
					false,
					ed,
					false);

			
				BlockTable bt =
					(BlockTable)tr.GetObject(
						db.BlockTableId,
						OpenMode.ForRead);

				BlockTableRecord ms =
					(BlockTableRecord)tr.GetObject(
						bt[BlockTableRecord.ModelSpace],
						OpenMode.ForWrite);

				foreach (SheetFrameCandidate frame in frames)
				{
					DrawBoundsRect(
						ms,
						tr,
						frame.Bounds,
						2, // 노랑
						"FRAME_USED_BOUNDS");
				}

				foreach (Entity ent in selectedEntities)
				{
					Bounds2D b =
						BricscadEntityTools.GetEntityBounds(ent);

					if (b == null || !b.IsValid)
						continue;

					DrawBoundsRect(
						ms,
						tr,
						b,
						1, // 빨강
						"ENTITY_USED_BOUNDS");
				}

				tr.Commit();
			

			ed?.WriteMessage(
				"\n[CoordCheck] FrameBounds=Yellow, EntityBounds=Red 표시 완료");
		}

		private static void DrawBoundsRect(
	BlockTableRecord ms,
	Transaction tr,
	Bounds2D b,
	short colorIndex,
	string layerName)
		{
			if (ms == null || tr == null)
				return;

			if (b == null || !b.IsValid)
				return;

			Polyline pl = new Polyline();

			pl.AddVertexAt(0, new Point2d(b.MinX, b.MinY), 0, 0, 0);
			pl.AddVertexAt(1, new Point2d(b.MaxX, b.MinY), 0, 0, 0);
			pl.AddVertexAt(2, new Point2d(b.MaxX, b.MaxY), 0, 0, 0);
			pl.AddVertexAt(3, new Point2d(b.MinX, b.MaxY), 0, 0, 0);
			pl.Closed = true;

			pl.Color =
				Color.FromColorIndex(
					ColorMethod.ByAci,
					colorIndex);

			ms.AppendEntity(pl);
			tr.AddNewlyCreatedDBObject(pl, true);
		}


	}
}