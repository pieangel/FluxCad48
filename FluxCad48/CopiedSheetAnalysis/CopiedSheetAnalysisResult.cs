using FluxCad48.CopiedSheetAnalysis.Thickness;

namespace FluxCad48.CopiedSheetAnalysis
{
	internal class CopiedSheetAnalysisResult
	{
		public int MainViewIndex { get; set; }
		public int ThicknessSideViewIndex { get; set; }

		public double? GeometryThickness { get; set; }
		public double? DimensionThickness { get; set; }
		public double? TextThickness { get; set; }

		public ThicknessMatchResult ThicknessMatchResult { get; set; }

		public string Message { get; set; }

		public CopiedSheetAnalysisResult()
		{
			MainViewIndex = -1;
			ThicknessSideViewIndex = -1;
			ThicknessMatchResult = ThicknessMatchResult.NotFound;
			Message = "";
		}
	}
}