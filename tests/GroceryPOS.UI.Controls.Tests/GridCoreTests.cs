using GroceryPOS.UI.Controls.Grids.Core;

namespace GroceryPOS.UI.Controls.Tests;

public class GridCoreTests
{
    private sealed record Row(string Code, string Name, decimal Price, int? Stock);

    private static readonly Row[] Rows =
    [
        new("SP001", "Sữa tươi Vinamilk 1L", 32000m, 12),
        new("SP002", "Đường trắng Biên Hòa 1kg", 25000m, null),
        new("SP003", "Bánh mì sandwich", 18000m, 0),
        new("SP004", "Ăn liền Hảo Hảo", 4500m, 120),
        new("SP005", "Dầu ăn Neptune 1L", 52000m, 8),
    ];

    private static GridDataView<Row> CreateView(IEnumerable<Row>? rows = null)
    {
        var view = new GridDataView<Row>(
        [
            new("Code", r => r.Code),
            new("Name", r => r.Name),
            new("Price", r => r.Price, SearchText: r => r.Price.ToString("N0")),
            new("Stock", r => r.Stock, Searchable: false),
        ]);
        view.SetItems(rows ?? Rows);
        return view;
    }

    [Theory]
    [InlineData("sua", "SP001")]
    [InlineData("DUONG bien", "SP002")]
    [InlineData("hao  hao", "SP004")]
    [InlineData("sp005", "SP005")]
    public void Search_is_accent_and_case_insensitive_and_needs_every_word(string search, string expectedCode)
    {
        var page = CreateView().Execute(new GridQuery().WithSearch(search));

        var row = Assert.Single(page.Items);
        Assert.Equal(expectedCode, row.Code);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public void Search_words_may_come_from_different_fields_in_any_order()
    {
        var page = CreateView().Execute(new GridQuery().WithSearch("1l neptune"));

        Assert.Equal("SP005", Assert.Single(page.Items).Code);
    }

    [Fact]
    public void Non_searchable_fields_are_ignored_and_empty_search_returns_all()
    {
        var view = CreateView();

        Assert.Empty(view.Execute(new GridQuery().WithSearch("120")).Items);
        Assert.Equal(5, view.Execute(new GridQuery().WithSearch("   ")).TotalCount);
    }

    [Fact]
    public void Sorts_text_with_vietnamese_collation()
    {
        var view = CreateView();
        var query = new GridQuery().ToggleSort("Name");

        var names = view.Execute(query).Items.Select(r => r.Code).ToList();

        // Vietnamese order is a, ă, â, b, c, d, đ … so Ăn < Bánh < Dầu < Đường < Sữa.
        Assert.Equal(["SP004", "SP003", "SP005", "SP002", "SP001"], names);
    }

    [Fact]
    public void Sort_cycles_ascending_descending_none_and_puts_nulls_first()
    {
        var view = CreateView();
        var asc = new GridQuery().ToggleSort("Stock");
        var desc = asc.ToggleSort("Stock");
        var none = desc.ToggleSort("Stock");

        Assert.Equal(["SP002", "SP003", "SP005", "SP001", "SP004"], view.Execute(asc).Items.Select(r => r.Code));
        Assert.Equal(["SP004", "SP001", "SP005", "SP003", "SP002"], view.Execute(desc).Items.Select(r => r.Code));
        Assert.False(none.IsSorted);
        Assert.Null(none.SortKey);
        Assert.Equal(Rows.Select(r => r.Code), view.Execute(none).Items.Select(r => r.Code));
    }

    [Fact]
    public void Sorting_another_column_starts_ascending_and_resets_page()
    {
        var query = new GridQuery { PageIndex = 3 }.ToggleSort("Price").ToggleSort("Price").WithPage(2).ToggleSort("Code");

        Assert.Equal("Code", query.SortKey);
        Assert.Equal(SortDirection.Ascending, query.SortDirection);
        Assert.Equal(0, query.PageIndex);
    }

    [Fact]
    public void Sorting_is_stable_for_equal_values()
    {
        var rows = Enumerable.Range(1, 10).Select(i => new Row($"SP{i:000}", "Cùng tên", 1000m, i % 2)).ToList();
        var page = CreateView(rows).Execute(new GridQuery { PageSize = 10 }.ToggleSort("Name"));

        Assert.Equal(rows.Select(r => r.Code), page.Items.Select(r => r.Code));
    }

    [Fact]
    public void Unknown_sort_key_keeps_source_order()
    {
        var page = CreateView().Execute(new GridQuery().ToggleSort("Missing"));

        Assert.Equal(Rows.Select(r => r.Code), page.Items.Select(r => r.Code));
    }

    [Fact]
    public void Compares_mixed_number_types_by_value()
    {
        Assert.True(GridDataView<Row>.CompareValues(5, 4.5m) > 0);
        Assert.True(GridDataView<Row>.CompareValues(2L, 2.5d) < 0);
        Assert.Equal(0, GridDataView<Row>.CompareValues(3, 3m));
    }

    [Fact]
    public void Duplicate_keys_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new GridDataView<Row>([new("Code", r => r.Code), new("Code", r => r.Name)]));
    }

    [Fact]
    public void Pages_and_clamps_to_last_page()
    {
        var rows = Enumerable.Range(1, 45).Select(i => new Row($"SP{i:000}", $"Hàng {i}", i, i)).ToList();
        var view = CreateView(rows);

        var second = view.Execute(new GridQuery().WithPage(1));
        var beyond = view.Execute(new GridQuery().WithPage(99));

        Assert.Equal(20, second.Items.Count);
        Assert.Equal("SP021", second.Items[0].Code);
        Assert.Equal(3, second.PageCount);
        Assert.True(second.HasPrevious);
        Assert.True(second.HasNext);
        Assert.Equal(2, beyond.PageIndex);
        Assert.Equal(5, beyond.Items.Count);
        Assert.False(beyond.HasNext);
        Assert.Equal(41, beyond.FirstItemNumber);
        Assert.Equal(45, beyond.LastItemNumber);
    }

    [Fact]
    public void Summary_uses_vietnamese_number_grouping()
    {
        var page = new GridPage<int>(Enumerable.Range(0, 20).ToList(), 1235, 1, 20);

        Assert.Equal("Hiển thị 21–40 / 1.235 dòng", page.Summary);
        Assert.Equal("Không có dữ liệu", GridPage<int>.Empty().Summary);
        Assert.Equal(1, GridPage<int>.Empty().PageCount);
        Assert.Equal(0, GridPage<int>.Empty().FirstItemNumber);
    }

    [Fact]
    public void Page_rejects_inconsistent_counts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridPage<int>([1, 2, 3], 2, 0, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridPage<int>([], 0, 0, 0));
    }

    [Fact]
    public void Map_keeps_paging_numbers()
    {
        var page = new GridPage<int>([1, 2], 42, 2, 20).Map(i => i.ToString());

        Assert.Equal(["1", "2"], page.Items);
        Assert.Equal(42, page.TotalCount);
        Assert.Equal(2, page.PageIndex);
    }

    [Theory]
    [InlineData(0, 50, 0)]
    [InlineData(2, 50, 0)]   // first row 41 → page 1 of 50
    [InlineData(3, 50, 1)]   // first row 61 → page 2 of 50
    [InlineData(5, 100, 1)]  // first row 101 → page 2 of 100
    public void Changing_page_size_keeps_first_visible_row(int pageIndex, int newSize, int expectedPage)
    {
        var query = new GridQuery { PageIndex = pageIndex }.WithPageSize(newSize);

        Assert.Equal(newSize, query.PageSize);
        Assert.Equal(expectedPage, query.PageIndex);
    }

    [Fact]
    public void Page_size_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridQuery().WithPageSize(0));
    }

    [Theory]
    [InlineData(0, 1, "1")]
    [InlineData(2, 7, "1 2 3 4 5 6 7")]
    [InlineData(0, 20, "1 2 3 4 5 … 20")]
    [InlineData(3, 20, "1 2 3 4 5 … 20")]
    [InlineData(4, 20, "1 … 4 5 6 … 20")]
    [InlineData(9, 20, "1 … 9 10 11 … 20")]
    [InlineData(15, 20, "1 … 15 16 17 … 20")]
    [InlineData(16, 20, "1 … 16 17 18 19 20")]
    [InlineData(19, 20, "1 … 16 17 18 19 20")]
    [InlineData(99, 20, "1 … 16 17 18 19 20")]
    [InlineData(4, 8, "1 … 4 5 6 7 8")]
    public void Page_links_show_window_around_current_page(int pageIndex, int pageCount, string expected)
    {
        var links = PageLinks.Build(pageIndex, pageCount);

        Assert.Equal(expected, string.Join(' ', links.Select(l => l is null ? "…" : (l + 1).ToString())));
        Assert.True(links.Count <= 7);
    }

    [Fact]
    public void Page_links_always_include_current_page_and_never_two_gaps_in_a_row()
    {
        for (var count = 1; count <= 30; count++)
        {
            for (var page = 0; page < count; page++)
            {
                foreach (var slots in new[] { 5, 7, 9 })
                {
                    var links = PageLinks.Build(page, count, slots);
                    Assert.Contains(page, links.Where(l => l is not null).Select(l => l!.Value));
                    Assert.Equal(0, links[0]);
                    Assert.Equal(count - 1, links[^1]);
                    Assert.True(links.Count <= slots);
                    for (var i = 1; i < links.Count; i++)
                    {
                        Assert.False(links[i] is null && links[i - 1] is null);
                        if (links[i] is not null && links[i - 1] is not null)
                        {
                            Assert.Equal(links[i - 1] + 1, links[i]);
                        }
                    }
                }
            }
        }
    }
}

public class GridPaletteTests
{
    public static TheoryData<string> ThemeNames => new() { "Light", "Dark" };

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void Grid_text_is_readable_on_rows_headers_and_selection(string name)
    {
        var theme = name == "Dark" ? Theming.Themes.Dark : Theming.Themes.Light;
        var colors = GridPalette.Resolve(theme);

        Assert.True(Theming.ColorMath.ContrastRatio(colors.Text, colors.Row) >= 4.5);
        Assert.True(Theming.ColorMath.ContrastRatio(colors.Text, colors.AlternateRow) >= 4.5);
        Assert.True(Theming.ColorMath.ContrastRatio(colors.SelectionText, colors.SelectionBackground) >= 4.5);
        Assert.True(Theming.ColorMath.ContrastRatio(colors.Text, colors.HoverRow) >= 4.5);
        Assert.True(Theming.ColorMath.ContrastRatio(colors.HeaderText, colors.HeaderBackground) >= 4.5);
        Assert.True(Theming.ColorMath.ContrastRatio(colors.EmptyText, colors.Background) >= 3.0);
    }

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void Selection_and_alternate_rows_are_distinguishable_from_plain_rows(string name)
    {
        var theme = name == "Dark" ? Theming.Themes.Dark : Theming.Themes.Light;
        var colors = GridPalette.Resolve(theme);

        Assert.NotEqual(colors.Row, colors.AlternateRow);
        Assert.NotEqual(colors.Row, colors.SelectionBackground);
        Assert.NotEqual(colors.AlternateRow, colors.SelectionBackground);
        Assert.True(Theming.ColorMath.ContrastRatio(colors.Row, colors.SelectionBackground) > 1.1);
    }
}

public class PropertyPathTests
{
    private sealed record Category(string Name);

    private sealed record Product(string Code, Category? Category);

    [Fact]
    public void Reads_simple_and_dotted_paths()
    {
        var product = new Product("SP001", new Category("Sữa"));

        Assert.Equal("SP001", PropertyPath.GetValue(product, "Code"));
        Assert.Equal("Sữa", PropertyPath.GetValue(product, "Category.Name"));
    }

    [Fact]
    public void Missing_property_or_null_in_path_gives_null()
    {
        var product = new Product("SP001", null);

        Assert.Null(PropertyPath.GetValue(product, "Category.Name"));
        Assert.Null(PropertyPath.GetValue(product, "Missing"));
        Assert.Null(PropertyPath.GetValue(null, "Code"));
        Assert.Null(PropertyPath.GetValue(product, ""));
    }
}
