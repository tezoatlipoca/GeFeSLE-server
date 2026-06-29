# GeFeSLE - Generic, Federated, Subscribable List Engine

<img width="300" height="" alt="A Sample List in the GeFeSLE web interface" src="https://github.com/user-attachments/assets/1e6298e3-1a9a-45e0-b3de-ef4a1adaa9f5" />

<img width="300" height="" alt="The same sample list in a Mastodon client (Phanpy)" src="https://github.com/user-attachments/assets/278dcf32-5ea6-4795-9fca-285cf1ed0a4b" />

The _sample list in the screenshots above_ can be [**seen LIVE** on our flagship/test/sample site **HERE**](https://lists.awadwatt.com/Federation%20Test.html) or in **Mastodon** at `@federation.test@lists.awadwatt.com`


### **Yet _another_ list making tool? Yes. **

BUT - in _THIS_ list making tool:
- _**FAST**_ single binary, self-hosted, linux and Windows
- each list is a static HTML page, an `.RSS` feed
- list items can be searched/filtered
- list management, list item editing, user management all through self-contained web interface OR REST API
- customize the appearance of everything with your own `CSS` stylesheets, custom page headers/footers
- lists can be public or private or shared only with friends
- invite friends to help curate your lists; "log in with" Google or Microsoft accounts, or using a Mastodon account
- delegate different levels of list control to different users: creator/listowner/contributor
- list items can be suggested by anonymous users and held in a moderation queue
- list items can be reported and are removed to a moderation queue

## Import/Export Capability

Besides lists being static HTML (with a little bit of Javascript and CSS) which can easily be saved to a single file with utilities/plugins like `SingleFile` for use offline or distribution,

- individual/all lists can be exported/imported via `.json`
- list users logged in with Microsoft Accounts can import List Items from their _Sticky NOtes_ folders  _(OneNote, other sources are future work)_
- list users logged in with Google Accounts can import List ITems from one of their _Task LIsts_ 
- list users logged in with a Mastodon Account can import List Items from their _Bookmarks_

Other sources of lists like Obsidian, Trello are possible future sources. 

## ActivityPub Federation
GeFeSLE is also federated
- users can follow lists just like other users
- list items appear as the list's "Posts"; boostable and bookmarkable
- suggestions received to the list actor's "inbox" are treated as suggestions and held in a moderation queue

Commenting on list items is being worked on right now. 

## Clients
- Linux / Windows system tray app: [GeFeSLE-systray](https://github.com/tezoatlipoca/GeFeSLE-systray)
- Browser plugins: (in progress)
- Linux / Windows (Powershell) CLI: (planned)

.. or build your own!

# How do I install it? 
- [installation](/Documentation/installation.md)
- [Configuring GeFeSLE / config file](/Documentation/Configuration.md)
- [Microsoft, Google, Mastodon - OAuth providers and import sources](/Documentation/google.microsoft.oauth.md)
- putting GeFeSLE behind NGINX
- MacOS?

# How do I use it? 
- [Basic use](/Documentation/usage.md)
- [managing users](Documentation/managing.users.md)
- [Moderation](Documentation/moderation.md)


