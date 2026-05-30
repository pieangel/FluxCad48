namespace FluxCad48.CopiedSheetAnalysis.ViewDetection
{
	internal enum SheetViewRole
	{
		Unknown = 0,

		MainView = 1,
		ProjectedView = 2,
		ThicknessSideView = 3,

		DetailView = 4,
		SectionView = 5,
		DimensionOnly = 6,
		TextOnly = 7,
		Noise = 8
	}
}