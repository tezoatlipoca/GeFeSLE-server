# GeFeSLE - Generic, Federated, Subscribable List Engine
Yet _another_ list making tool? Yes. **BUT** with the primary goals of:
* designed around sharing and collaboration: with friends, communities or the internet at large
* self-hosted: lightweight and self-contained single binary (except for the runtime environment)
* simple html+css presentation: view lists on any device (if it can run a browser)
  * human readable static addressing (1) e.g. `myserver.mydomain.org/Big Fat List Of The Year.html`
* single-user mode: lists just for you, or
  *   multi-user: create local accounts if you want, or allow friends to login from other platforms with OAuth
* get ur data **IN**: import lists from anywhere
  * json, xml
  * Mastodon Bookmarks
  * Google Tasks
  * Microsoft Sticky Notes
  * .. with more planned in the future: Trello (boards, swimlanes), Pocket, Google Keep/Saved, Microsoft OneNote, browser bookmarks, 
* get ur data **OUT**:
  * html+css (and a little bit of javascript for filtering); and it prints to PDF just fine without any funny stuff
  * json, xml, text, Excel
* self-hosted web interface for administration and list curation
  * browser plugins (Firefox for now) to quickly add new list items - no matter what page you're on; take a snip of the current browser tab to add to your new item
  * Windows systray widget to add new list items - wouldn't it be nice if you could add an item to Trello without opening Trello?
  * fully documented REST API - don't like our apps? write your own.

(1) so long as you don't change the list name
 
Working on the assumption that fetching and browsing the list should be _fast_, and modifications 
to lists or the items in in them are infrequent, modifications to list/items triggers regeneration of a static html
page and an RSS feed xml for each list. 

Media can be attached to list items via file uploads for sharing. Media referenced in imported items can stay referenced, 
or be downloaded for local use. 

Both static pages or attached media files respect list visibility that list owners select: 
* Private - only the owner/creator of a list can see
* List Owner friends - other users you designate have full control over lists including granting access to new friends
* Contributor friends - who can add/modify items and lists but cannot delete things or grant access to new friends
* Public - lists are visible to all, even if they don't have a login
 
## Federated? (roadmap: v0.2.0)
Each List gets an Activity Pub handle that you can follow from any Federated platform. 
e.g. `Steve's List of Where To Eat In Cleveland` -> `@cleveland_eats@gefesle.blahaj.zone`

Every new list item (or update or deletion) puts an appropriate toot into your feed.
Replies to those toots, get threaded as comments on the lists (subject to various controls and settings
to make sure noone's toots are recorded forever someplace they didn't intend). 
