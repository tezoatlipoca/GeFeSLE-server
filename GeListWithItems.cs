using System.Text.Json;
using Microsoft.EntityFrameworkCore;

public class GeListWithItems
{
    public GeList List { get; set; }
    public IEnumerable<GeListItem> Items { get; set; }

    // constructor that takes a db context and a list id
    public GeListWithItems(GeFeSLEDb db, int listId)
    {
        List = db.Lists.Find(listId);
        if(List == null)
        {
            throw new ArgumentNullException("GeLIstWithItems: list is null/invalid");
        }
        Items = db.Items.Where(item => item.ListId == listId).ToList();
    }
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

    // members to Export the list and its items to JSON for transfer or backup
    

    public async Task ExportListJSON(GeFeSLEDb db, string outFile)
    {
        DBg.d(LogLevel.Trace, $"GenerateJSON {List.Id}- {List.Name} --> {outFile}");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true // Set WriteIndented to true for pretty formatting
        };
        var json = JsonSerializer.Serialize(this, options);
        await File.WriteAllTextAsync(outFile, json);
        DBg.d(LogLevel.Trace, $"JSON generated.");
    }

    // for now keep simple; this is really just for import and export. 
    // importing will create new List and items, creating new Ids for them
    public static async Task<GeListWithItems?> ImportListJSON(GeFeSLEDb db, string inFile, bool keepDates)
    {
        try
        {
            DBg.d(LogLevel.Trace, $"ImportJSON  <-- {inFile}");

            try
            {
                var json = await File.ReadAllTextAsync(inFile);
                var inList = JsonSerializer.Deserialize<GeListWithItems>(json);
                if (inList == null)
                {
                    DBg.d(LogLevel.Error, $"ImportJSON: {inFile} is not a valid JSON file");
                    return null;
                }
                else
                {
                    // create a new list and items
                    var newList = new GeList
                    {
                        Name = inList.List.Name,
                        Comment = inList.List.Comment,
                        CreatedDate = keepDates ? inList.List.CreatedDate : DateTime.Now,
                        ModifiedDate = keepDates ? inList.List.ModifiedDate : DateTime.Now
                    };
                    db.Lists.Add(newList);
                    await db.SaveChangesAsync();
                    foreach (var item in inList.Items)
                    {
                        var newItem = new GeListItem
                        {
                            Name = item.Name,
                            Comment = item.Comment,
                            CreatedDate = keepDates ? item.CreatedDate : DateTime.Now,
                            ModifiedDate = keepDates ? item.ModifiedDate : DateTime.Now,
                            Tags = item.Tags,
                            ListId = newList.Id
                        };
                        db.Items.Add(newItem);
                        
                    }
                    await db.SaveChangesAsync();
                    var newItems = await db.Items.Where(item => item.ListId == newList.Id).ToListAsync();

                    return new GeListWithItems(newList, newItems);
                }
            }
            catch (Exception ex)
            {
                DBg.d(LogLevel.Error, $"ImportJSON: {inFile} is not a valid JSON file: {ex.Message}");
                return null;
            }

        }
        catch (FileNotFoundException ex)
        {
            DBg.d(LogLevel.Error, $"ImportJSON: {inFile} does not exist: {ex.Message}");
            return null;
        }
    }
    
}