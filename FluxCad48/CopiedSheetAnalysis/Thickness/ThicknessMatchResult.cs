namespace FluxCad48.CopiedSheetAnalysis.Thickness
{
	internal enum ThicknessMatchResult
	{
		NotFound = 0,
		Match = 1,
		Mismatch = 2,
		GeometryOnly = 3,
		DimensionOnly = 4,
		TextOnly = 5
	}
}