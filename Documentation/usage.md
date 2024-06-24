# First Run
To run GeFeSLE from the command line, be sure to specify a configuration file with the `--config=` parameter:
`%> ./GeFeSLE --config=./config.json`

GeFeSLE won't run without a config file.. which it needs to specify, at the very least, the name of the database file to use. 
If the database file in question _doesn't exist_, it will be created; in either case, the required database junk (schema) is created IN it; if the schema from an older GeFeSLE install is found, it is _migrated_ (so if you're re-using an old db file, make a backup).

The only other minimum setup required is to specify a super user `backdooradmin` in the config file:

```
"Users": {
        "backdooradmin": {
            "username": "<name>",
            "password": "<pwd>",
            "role": "SuperUser"
        }
    }
```
This user can _always_ log in, can _always_ see all content. If you're running GeFeSLE as a single user application, this is the only user account you need really. 

## The wwwroot
After reading in the config file, the GeFeSLE service checks to see if the `wwwroot` (or the default of `./wwwroot` if its not specified in the config file) exists. 
This folder will hold the generated HTML, xml and json files for all lists, as well as the upload folders for any item attachments. 
It also contains some static `.html` and `.js` files that are served as part of the web interface; if _these_ files don't exist, they're created in `wwwroot` - if they exist, they're overwritten on application startup. 

## Web interface
Once the application is running, point a web browser to where you've configured it to listen.
If you're running GeFeSLE on the same computer as you are browsing from then it will be at `http://localhost:<port>` where `<port>` is the `Port` parameter under `ServerSettings` in the config file. 
You should see (usage.1.PNG)

The placeholders for page header, site title, page footer and owner can all be customized in the config file. 

### login
Takes you to the login screen where you can login with local credentials.
The other 3 buttons are to OAuth login with a Google, Microsoft or Mastodon account; however to do that we have to [configure those OAuth sources first](google.microsoft.oauth.md).
.. and THEN we have to add those user accounts as users in our database allowed to login.

For now, the `backdooradmin` user specified in our config file is the only user we have; log in with those credentials. 

### Main / index page
This page shows all lists a user is allowed to view. 
* change password - goes to the password change screen
* edit users (SuperUsers) - to manage other users 
* Add new list (ListOwners+)- opens the Edit List page to add a new list
* REGEN - regenerates all index and list page HTML files.

For each list visible on this index there are buttons/links to 
* view the list
* edit
* delete - deleting a list (after the _Are You Sure?_ prompt) is immediate and deletes all list items as well

### List View
Each List page is a static HTML file, with a static URL from the root of your GeFeSLE instance. 
Changing the NAME of the list changes its URL (Future work: add a `forward old list URLs to new URLs` feature.)

* Edit this list - see "List Edit"
* Add new item - see "Adding Items"
* ++Masto Bookmarks - if the user is logged in using OAuth from a Mastodon account they can import their Mastodon bookmarks to THIS list.
* ++Microsoft Sticky Notes - if the user is logged in using OAuth from a Microsoft account they can import their Windows/Outlook Sticky Notes
* ++Google Tasks - if the user is logged in using OAuth from a Google account they can import the Tasks from one of their Google Task LIsts
* Regen - regenerates just the HTML for THIS list.
* RSS Feed - clicks through to the RSS xml page for this list (which is really just `<hostname>/<list name>.xml` instead of `.html`)
* JSON - dumps the list as a JSON file to do whatever you want with
* SUGGEST - If the viewer doesn't have any login credentials, or does, but does not have a role or list ownership permissions to add items to this list, opens the Add/Edit Item screen. See "Suggestions" below.

### FIlter bar
The result bar - besides showing success/failure of any edit operations - shows how many items (of the total) in this list are currently being show or filtered.
The two filter fields (Search Text, Search Tags) allow you to filter on items containing that text or containing those tags respectively. 
The full list is always present in the browser, any filters applied merely hide or show items that match the filter critieria - this was an explicit design goal to allow users to SAVE the list locally but still use their browser's page search and these filter controls to quickly navigate and find.

### Items
Each list _item_ header has a bookmark icon that when clicked saves the URL to _that_ item to the clipboard as an HTML anchor/bookmark: `http://<hostname>/<list name>.html#<itemid>`
Besides its name/url and its main body content, each item has
* creation/last modified date
* Move - right click on this brings up a context menu showing all other Lists the user has edit permissions on; selecting one of those lists, moves THIS item to THAT selected list. This list page is regenerated and refreshed immediately.
* Edit - see "Editing Items"
* Delete - deletes this item; regenerates this page.
* Report - marks this item as invisible in this list (and the list is regenerated; the repoted item will dissappear); prompts you for reasons why you're reporting this item and then flags the item to the attention of a SuperUser.

Item Tags:
Tags are keyword labels that can be used to rapidly sort and filter items in a list. 
Click in the body of the tags area to add a new one (duplicates are ok).
Click ON a tag to remove it. 

### Adding/Editing List Items
If you have Contributor or Listowner role for a list then you can add or modify items in that list. 

* New/Update - all this does is trigger whether the form clears when you click add/update
* ID & List ID - read only fields showing the ID of the item and the list its in.
* Visible - if checked the item shows in the list html, RSS and json. If not its invisible. ⚠️if you change a visible item to invisible there's currently no way of finding it again unless you have a bookmark to it in this page e.g. `http://localhost:7036/_edit.item.html?listid=3&itemid=1`. When a user REPORTs an item it is preemptively marked as invisible, but a moderation ticket is created that references this item for SuperUsers to use.
* Name - item title, summary, URL.. whatever. IF the Name is recognizeable as an address, in the list view it is automaticlly rendered as a clickable link.
* comment (body) - Put whatever you want here; Markdown supported. In the List View, CSS is used to size each item "row" to take up no more than a 1/3 of the screen and any overflow content is hidden; but the full item can be seen simply by clicking on the body of the item comment.
* tags - same tags as shown in the list view, except delimited by space. ⚠️BUG: if you add a single tag that _contains_ spaces in list view, it is converted into individual single word tags by the Add/Edit Item page.
* Add NEW or UPDATE - submits the form, saves your changes
* Attach a file - select a file then click Upload. The file is uploaded into a subfolder of the `wwwroot` according to username: `wwwroot/uploads/<username>`; then the URI to the uploaded file is pasted into the item's comment body in Markdown notation. ⚠️IMPROVEMENT: it pastes the markdown assuming the uploaded file is an image, however if its not an image the rendered text isn't a link to the file. Detect the extension of the uploaded file and modify the markdown accordingly if it is NOT an image, just make it a link to the file.

 ### Adding/Editing LISTS
 



### Debug commands (TEMPORARY)
The red buttons at the top of each page are debug utilities that will be suppressed with a config parameter once we get closer to v1.0.
* Session - calls `/session` which is a dump of all http request parameters and any session cookies for the current user.
* KILL Session - calls `/me/delete` which trashes the active user session; equivalent to a `LogOUt` (which we don't have yet)
* REGEN - regenerates List `.html` and rss pages for each list and the index page.
* File Orphans - recursively scans every file in `wwwroot`; identifies `protected` file, which are restored when the application starts or a clean is invoked, files which are generated for a List, and uploads which are either referenced by an item in a list, or are NOT referenced by any item in a list (an orphan)
* EXPORT/IMPORT - intermediary backup and restore function - exports/imports every list (and their items) as json

