# GeFeSLE - Generic, Federated, Subscribable List Engine
Yet _another_ list making tool? Yes. **BUT** with the primary goals of:
* designed around sharing and collaboration: with friends, communities or the internet at large
* self-hosted: lightweight and self-contained single binary (except for the runtime environment)
* simple html+css presentation: view lists on any device (if it can run a browser)
  * human readable static addressing (1) e.g. `myserver.mydomain.org/Big Fat List Of The Year.html`
  * customize your site with page headers/footers and your own CSS stylesheet (without a plugin; eat _that_ WordPress)
  * filter lists by text or keywords (hashtags)
  * one-click add/remove tags; one-click move items between lists (permissions allowing of course)
* single-user mode: lists just for you, or
  *   multi-user: create local accounts if you want, or allow friends to login from other platforms with OAuth (Google, Microsoft and Mastodon)
* get ur data **IN**: import lists from anywhere
  * json, xml
  * Mastodon Bookmarks
  * Google Tasks
  * Microsoft Sticky Notes
  * .. with more planned in the future: Trello (boards, swimlanes), Pocket, Google Keep/Saved, Microsoft OneNote, browser bookmarks, 
* get ur data **OUT**:
  * html+css (and a little bit of javascript for filtering); and it prints to PDF just fine without any funny stuff
  * json, xml, text, Excel
  * .. write your own!
* self-hosted web interface for administration and list curation
  * browser plugins (Firefox for now) to quickly add new list items - no matter what page you're on; take a snip of the current browser tab to add to your new item
  * Windows systray widget to add new list items - wouldn't it be nice if you could add an item to Trello without opening Trello?
  * (soon to be fully documented) REST API - don't like our apps? write your own.

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

## Future Work (v0.3.0+)
- accessibility and localization support (those are table stakes)
- list flavours & behaviours:
  - a list of websites is a webring is it not?
  - a list of songs or videos _could_ be a playlist
- list items get "garnishes" to qualify, reinforce, refute or challenge an item (or a whole list) e.g:
  - "we ate here the food was excellent"
  - "I just checked this link it 404'd" or "last verified: `<date>`"
  - "this author said `<insert objectionable behaviour here>`, may want to think about their inclusion on this list"
  - "I can provide a second source for this quotation: `<link>`"  
- "in a box" configuration. Taking inspiration from the [Mail-in-a-box](https://mailinabox.email/) and similar one-click-self-host-setup projects, GeFeSLE will be able to self-configure (or hand hold the user) to:
  - install from a package on the host of your choice
  - configure firewalls (and help with reverse proxy if necessary)
  - custom domain registration
  - dynamic dns setup + test
  - external ping-back "am I reachable?" checks
  - certificates registration and management
  - master instance lists of lists (opt-in of course) for list discovery and Federation
  - search engine friendly: robots.txt, sitemaps, crawl-me requests
 - GeFeSLE already hosts static pages with _file level_ access control restrictions and file uploading capabilities. Its a hop, skip and a jump from here to having a single binary webserver for static HTML and Javascript hosting. Bring back Web 1.0 (just with better CSS support)!

## Um, ok. But Why? 
I left Twitter in November of 2022 and discovered the Fediverse. I noticed that - because there's no algorithm.. or rather YOU and your network are the algorithm - discovery is somewhat manual. When Twitter started suspending journalists, many folks started "Lists of Journalists to follow on Mastodon/the Fediverse". Other people started curating really amazing lists of Very Cool Things. These lists were in a WordPress blog, a Substack or maybe a Google Sheet - what if someone starts a list that one of those services took objection to, like "Alternatives to WordPress"? Or if the user switched platforms (like the recent Substack exodus), links TO the list would bork. 

After a few months of using Mastodon, and bookmarking so many interesting toots, I realized that I had no way to curate them! There's no export from your Mastodon bookmarks outside of using the API and exporting the raw JSON. (There is the excellent [Pocket Toots FIrefox plugin](https://addons.mozilla.org/en-US/firefox/addon/pockettoots/) which exports _new_ bookmarks to Pocket, but its not retroactive.)

Have your grocery list in Trello? Great! Now you have to use the Trello app to access your grocery list! Ok, I just need to add milk- TOO BAD! Its now time for two factor authentication! Can't remember that email address password? TWO two-factor authentications (ah ah ah!) arrg. Ill just write "milk" on my arm in Sharpie. _Ok smart guy just use a note app on your smartphone!_ Sure, now my family can't add stuff - or rather my grocery list is now a string of text messages. We can do better. 

Lastly, there is the (**enshittification** problem)[https://pluralistic.net/2023/01/21/potemkin-ai/#hey-guys]: what happens to your content when the platform that hosts it turns evil: spams your readers with too many ads, invites the nazis in or just removes or bans your content because of something you said? (That should be up to YOUR readers to decide, not the platform!) The way to fight enshittification is to **self-host**. 

This service will only be about making, and hosting, _lists of things_ and will only add features in service of that end.  

## State of the Project
(as of 2024-04-08)
**Working towards v0.1.0 MVP**
- GeFeSLE engine is an ASP.NET 8.0 C# executable (yes, we totally started with the ASP.NET 8.0 Core "Minimal API" tutorial)
- GeFeSLE FireFox plugin `<link>` CURRENTLY BROKEN (API refactoring)- only allows login, list selection and addition of items (body in Markdown); does have "receipt mode" where it takes a snip of the open browser tab, uploads it to server and adds the image link to the item Markdown. 
- lists can be created, modified and removed; list description is Markdown
- list items can be created, modified and removed; item body is Markdown
- when viewing a list with appropriate permissions, tags can be one-click added/removed; items can be one-click moved to another list (that you likewise have permission on)
- list level export/import to/from json (I wouldn't call this a full export/import yet; for one it doesn't include any local attachments/images; no users etc.)
- list items can be imported to lists from: Mastodon Bookmarks, Microsoft/Windows Sticky-Notes, Google Tasks (gave up on browser bookmarks: Netscape-bookmarks-file-1 is a tough nut)
- users can be created, modified and removed; password reset; assign roles: SU, List Owner, List Contributor
- users can log in from OAuth: Mastodon, Microsoft and Google (although they have to ALSO be lised in the database to have a role assigned and do anything)

### TODO Before v0.1.0 MVP:
- Refactor API according to CRUD and HATEOAS; ensure meaningful HTTP error codes and consistent data returns; consistent access restriction on endpoints by user role; probably should verson control too e.g. `/endpoint` -> `/api/v1/endpoint`. 
- lock down all static file serving, restricting access on basis of list viewership (images/media uploads currently are not considered protected files) #2
- #72
- #74


