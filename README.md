# GeFeSLE - Generic, Federated, Subscribable List Engine
Yet _another_ list making tool? Yes. **BUT** with the primary goals of:
* designed around sharing and collaboration: with friends, communities or the internet at large
* self-hosted: lightweight and self-contained (except for a runtime environment)
* simple html+css presentation: view lists on any device (if it can run a browser)
* single-user mode: lists just for you, or
  *   multi-user: create local accounts if you want, or allow friends to login from other platforms with OAuth
* get ur data **IN**: import lists from anywhere
  * json, xml (more coming soon)
  * Mastodon: bookmarked statuses
  * browser bookmarks
  * Microsoft: Sticky Notes, OneNote
  * Google: Tasks, (??)
  * Trello (whole boards or just a swimlane)
* get ur data **OUT**:
  * html+css (and a little bit of javascript for filtering); and it prints to PDF just fine without any funny stuff
  * json, xml, text, Excel

Working on the assumption that fetching and browsing the list should be _fast_, and modifications 
to lists or the items in in them are infrequent, modifications to list/items triggers regeneration of a static html
page for each list. 

Media can be attached to list items via file uploads for sharing. Media referenced in imported items can stay referenced, 
or be downloaded for local use. 

Both static pages or attached media files respect list visibility that list owners select: 
* Private - only the owner/creator of a list can see
* List Owner friends - other users you designate have full control over lists including granting access to new friends
* Contributor friends - who can add/modify items and lists but cannot delete things or grant access to new friends
* Public - lists are visible to all, even if they don't have a login
 
## Federated eh?
Yep - whenever 
