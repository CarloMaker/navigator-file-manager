using Navigator.Core.Models;
using Navigator.Presentation.ViewModels;

namespace Navigator.Presentation.Tests;

public class PanelLayoutTests
{
    private static PanelId[] Ids(PanelLayoutViewModel layout, PanelDock dock) =>
        layout.GetVisible(dock).Select(p => p.Id).ToArray();

    [Fact]
    public void Default_layout()
    {
        var layout = new PanelLayoutViewModel();

        Assert.Equal(new[] { PanelId.Bookmarks, PanelId.Sessions, PanelId.FolderTree }, Ids(layout, PanelDock.Left));
        Assert.Equal(new[] { PanelId.QuickView, PanelId.Filters }, Ids(layout, PanelDock.Right));
        Assert.False(layout.DropStack.IsVisible);
    }

    [Fact]
    public void Move_to_the_other_side_at_a_position()
    {
        var layout = new PanelLayoutViewModel();

        layout.Move(layout.Bookmarks, PanelDock.Right, 1);

        Assert.Equal(new[] { PanelId.Sessions, PanelId.FolderTree }, Ids(layout, PanelDock.Left));
        Assert.Equal(new[] { PanelId.QuickView, PanelId.Bookmarks, PanelId.Filters }, Ids(layout, PanelDock.Right));
    }

    [Fact]
    public void Move_up_and_down_in_the_same_side()
    {
        var layout = new PanelLayoutViewModel();

        layout.MoveDown(layout.Bookmarks);
        Assert.Equal(new[] { PanelId.Sessions, PanelId.Bookmarks, PanelId.FolderTree }, Ids(layout, PanelDock.Left));

        layout.MoveUp(layout.FolderTree);
        Assert.Equal(new[] { PanelId.Sessions, PanelId.FolderTree, PanelId.Bookmarks }, Ids(layout, PanelDock.Left));
        Assert.False(layout.CanMoveDown(layout.Bookmarks));
        Assert.False(layout.CanMoveUp(layout.Sessions));
    }

    [Fact]
    public void Moving_a_hidden_panel_shows_it()
    {
        var layout = new PanelLayoutViewModel();

        layout.Move(layout.DropStack, PanelDock.Left, 0);

        Assert.True(layout.DropStack.IsVisible);
        Assert.Equal(PanelId.DropStack, Ids(layout, PanelDock.Left)[0]);
    }

    [Fact]
    public void Hiding_raises_layout_changed_once()
    {
        var layout = new PanelLayoutViewModel();
        var changes = 0;
        layout.LayoutChanged += (_, _) => changes++;

        layout.Hide(layout.Sessions);

        Assert.Equal(1, changes);
        Assert.DoesNotContain(PanelId.Sessions, Ids(layout, PanelDock.Left));
    }

    [Fact]
    public void Capture_and_apply_round_trip()
    {
        var original = new PanelLayoutViewModel();
        original.MoveToOtherSide(original.FolderTree);
        original.QuickView.IsVisible = false;
        original.SetWeights([(original.Filters, 420.0)]);

        var restored = new PanelLayoutViewModel();
        restored.Apply(original.Capture());

        Assert.Equal(Ids(original, PanelDock.Left), Ids(restored, PanelDock.Left));
        Assert.Equal(Ids(original, PanelDock.Right), Ids(restored, PanelDock.Right));
        Assert.False(restored.QuickView.IsVisible);
        Assert.Equal(420.0, restored.Filters.Weight);
    }

    [Fact]
    public void Reset_restores_the_default()
    {
        var layout = new PanelLayoutViewModel();
        layout.MoveToOtherSide(layout.Bookmarks);
        layout.Hide(layout.FolderTree);

        layout.Reset();

        Assert.Equal(new[] { PanelId.Bookmarks, PanelId.Sessions, PanelId.FolderTree }, Ids(layout, PanelDock.Left));
    }

    [Fact]
    public void Quick_view_can_go_below_the_file_list_and_survives_a_restart()
    {
        var layout = new PanelLayoutViewModel();

        layout.MoveTo(layout.QuickView, PanelDock.Bottom);
        layout.Move(layout.Filters, PanelDock.Bottom, 0);

        Assert.Equal(new[] { PanelId.Filters, PanelId.QuickView }, Ids(layout, PanelDock.Bottom));
        Assert.Empty(Ids(layout, PanelDock.Right));

        layout.MoveDown(layout.Filters);
        Assert.Equal(new[] { PanelId.QuickView, PanelId.Filters }, Ids(layout, PanelDock.Bottom));

        var restored = new PanelLayoutViewModel();
        restored.Apply(layout.Capture());
        Assert.Equal(new[] { PanelId.QuickView, PanelId.Filters }, Ids(restored, PanelDock.Bottom));
        Assert.Equal(new[] { PanelId.Bookmarks, PanelId.Sessions, PanelId.FolderTree }, Ids(restored, PanelDock.Left));
    }
}
