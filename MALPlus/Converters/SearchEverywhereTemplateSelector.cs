using Microsoft.Maui.Controls;
using MALPlus.Models.Models.Search;

namespace MALPlus.Converters;

public class SearchEverywhereTemplateSelector : DataTemplateSelector
{
    public DataTemplate CategoryTemplate { get; set; }
    public DataTemplate ItemTemplate { get; set; }
    public DataTemplate SeparatorTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        if (item is SearchCategoryItem)
            return CategoryTemplate;
        if (item is SearchEverywhereSeparator)
            return SeparatorTemplate;
        return ItemTemplate;
    }
}
