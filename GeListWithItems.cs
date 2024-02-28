public class GeListWithItems
{
    public GeList List { get; set; }
    public IEnumerable<GeListItem> Items { get; set; }

    // constructor that ensures the list and items are not null
    public GeListWithItems(GeList list, IEnumerable<GeListItem> items)
    {
        if (list == null)
        {
            throw new ArgumentNullException("GeLIstWithItems: list is null");
        }
        if (items == null)
        {
            throw new ArgumentNullException("GeLIstWithItems: items is null");
        }
        List = list;
        Items = items;
    }
}