using Photos.Core;
using Xunit;

namespace Photos.Tests
{
    public class PrivateFolderRulesTests
    {
        [Fact]
        public void MarkedFolderCoversItself()
        {
            Assert.True(PrivateFolderRules.Covers("0/c:\\pics", "0/c:\\pics"));
        }

        [Fact]
        public void MarkedFolderCoversItsSubfolders()
        {
            Assert.True(PrivateFolderRules.Covers("0/c:\\pics", "0/c:\\pics\\2020"));
        }

        [Fact]
        public void MarkedFolderDoesNotCoverSiblingWithSharedPrefix()
        {
            Assert.False(PrivateFolderRules.Covers("0/c:\\pics", "0/c:\\picsold"));
        }

        [Fact]
        public void SourceRootCoversEverythingInThatSourceOnly()
        {
            Assert.True(PrivateFolderRules.Covers("0/", "0/c:\\anything"));
            Assert.False(PrivateFolderRules.Covers("0/", "1/c:\\anything"));
        }
    }

    public class SlideshowPlannerTests
    {
        [Fact]
        public void SpreadsStartsEvenly()
        {
            Assert.Equal(new[] { 0, 5 }, SlideshowPlanner.StartIndices(10, 2));
        }

        [Fact]
        public void UnevenCountStillSpreadsStarts()
        {
            Assert.Equal(new[] { 0, 2, 4 }, SlideshowPlanner.StartIndices(7, 3));
        }

        [Fact]
        public void NoScreensGivesNoStarts()
        {
            Assert.Empty(SlideshowPlanner.StartIndices(10, 0));
        }

        [Fact]
        public void NoPhotosStartsAtZero()
        {
            Assert.Equal(new[] { 0, 0 }, SlideshowPlanner.StartIndices(0, 2));
        }
    }

    public class RankingEngineTests
    {
        [Fact]
        public void SyncDropsPhotosThatAreNoLongerLive()
        {
            var state = new RankingState();
            RankingEngine.Sync(state, new ulong[] { 1, 2, 3 }, new Random(1));
            RankingEngine.Sync(state, new ulong[] { 1, 2 }, new Random(1));
            Assert.DoesNotContain(3UL, state.Order);
        }

        [Fact]
        public void ProgressCountsDistinctLivePhotos()
        {
            var progress = RankingEngine.GetProgress(new RankingState(), new ulong[] { 5, 5, 6 });
            Assert.Equal(2, progress.Total);
        }
    }
}
