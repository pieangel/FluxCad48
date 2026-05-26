using System.Collections.Generic;

namespace FluxCad48.ShapeViewAnalysis
{
	public sealed class SelectedShapeViewAnalysisResult
	{
		public SelectedShapeViewSet SelectedSet { get; set; }

		public List<ViewIsland> ViewIslands { get; private set; }

		public List<string> Warnings { get; private set; }

		public SelectedShapeViewAnalysisResult()
		{
			SelectedSet = new SelectedShapeViewSet();
			ViewIslands = new List<ViewIsland>();
			Warnings = new List<string>();
		}
	}
}