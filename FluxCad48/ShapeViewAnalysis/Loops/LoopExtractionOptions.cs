namespace FluxCad48.ShapeViewAnalysis.Loops
{
	public sealed class LoopExtractionOptions
	{
		public double EndpointTolerance { get; set; }
		public double GapTolerance { get; set; }
		public double ClosureTolerance { get; set; }

		public double ArcStepDegrees { get; set; }
		public double MaxSegmentLength { get; set; }

		public bool EnableGapHealing { get; set; }
		public bool IncludeInteriorDivider { get; set; }

		public LoopExtractionOptions()
		{
			EndpointTolerance = 0.5;
			GapTolerance = 1.0;
			ClosureTolerance = 1.0;

			ArcStepDegrees = 8.0;
			MaxSegmentLength = 2.0;

			EnableGapHealing = true;
			IncludeInteriorDivider = false;
		}
	}
}