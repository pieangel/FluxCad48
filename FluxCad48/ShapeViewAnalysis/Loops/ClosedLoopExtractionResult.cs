using System.Collections.Generic;
using System.Linq;
using FluxCad48.Geometry;

namespace FluxCad48.ShapeViewAnalysis.Loops
{
	public sealed class ClosedLoopExtractionResult
	{
		public List<Segment2D> InputSegments { get; private set; }
		public List<ClosedLoopCandidate> ClosedLoops { get; private set; }
		public List<ClosedLoopCandidate> OpenChains { get; private set; }
		public List<string> Warnings { get; private set; }

		public ClosedLoopExtractionResult()
		{
			InputSegments = new List<Segment2D>();
			ClosedLoops = new List<ClosedLoopCandidate>();
			OpenChains = new List<ClosedLoopCandidate>();
			Warnings = new List<string>();
		}

		public bool IsClosed
		{
			get { return ClosedLoops.Count > 0; }
		}

		public int OuterLoopCount
		{
			get { return ClosedLoops.Count(x => !x.IsHole); }
		}

		public int HoleLoopCount
		{
			get { return ClosedLoops.Count(x => x.IsHole); }
		}

		public int OpenChainCount
		{
			get { return OpenChains.Count; }
		}

		public ClosedLoopCandidate LargestClosedLoop
		{
			get
			{
				return ClosedLoops
					.Where(x => !x.IsHole)
					.OrderByDescending(x => x.AbsArea)
					.ThenByDescending(x => x.Bounds.Area)
					.FirstOrDefault();
			}
		}

		public Bounds2D LargestLoopBounds
		{
			get
			{
				ClosedLoopCandidate loop = LargestClosedLoop;
				return loop != null ? loop.Bounds : new Bounds2D();
			}
		}

		public double LargestLoopArea
		{
			get
			{
				ClosedLoopCandidate loop = LargestClosedLoop;
				return loop != null ? loop.AbsArea : 0.0;
			}
		}

		public void AddWarning(string message)
		{
			if (!string.IsNullOrWhiteSpace(message))
				Warnings.Add(message.Trim());
		}
	}
}