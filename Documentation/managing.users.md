# Managing Users
_The user management page is still rather basic_
Only SuperUsers can manage user accounts. 
![Example of editing users](edit.users.PNG)

The top shows a list of all users in the database's Microsoft Identity schema, along with a link to delete that user (you'll be prompted to confirm). Clicking on the user name populates the form below with their particulars. 

* User ID is a GUID from the Identity schema and is created automatically (and can't be changed - it doesnt matter much anyway). 
* Username - if you're creating a local user this can be whatever you want. If this is the email or account of someone you want to log in using OAuth from another service, it should be the account from that other service. If you leave it blank but provide an email address below, it will use the email as the account username.
* new password - allows you (the SuperUser) to overwrite any user's password (no reset-users-pwd yet)
* Email - doesn't do anything (yet), but is used as username if that field is left empty when you create a new user.
* Role - SuperUser\ListOwner\Contributor - a _user_ is allocated zero, one or multiple roles. This is independant from, but relevant to their assigned roles on any particular list. The role you assign here essentially gatekeeps what _system_ features they're allowed to use, not what they're allowed to perform on any particular list, although there is some overlap - See "User roles vs. user+list roles" below. Select the role you want to assign/deassign to/from this user and click the appropriate button.
* Update user - applies any change to username or email; saves the form, reloads the page. 

## User roles vs. user+list roles

When you assign a role (SuperUser, Listowner, Contributor) to a user account, that role assignment is essentially used as the check to see if that user is allowed to use a particular feature (or behind the scenes, load a static page or call the relevant API endpoint.)
For example, if you have been assigned the ListOwner role, that means that you can access the pages (and API endpoints) that deal with creating/modifying lists - e.g. the Edit List Page -  _regardless_ of any user role assignment on any particular List. 

The practical effect of this is that a ListOwner could, in theory gain access to the Edit List Page, however if they tried to load, view or edit a list that they hadn't also been assigned the ListOwner role on, nothing would happen (other than error messages).  A user with Contributor role can VIEW a list that is marked as Contributors only, but may not be able to add/edit/remove items on that list unless they too are assigned as a Contributor OF that list. 

What is happening here is the former (user) role is stored in the user session; this was a deliberate design decision to front-load the coarse _restriction of access_ to various static pages up to the Kestrel middleware, which could do it much more efficiently, as opposed to hitting the database everytime for `is the user allowed to use this page? to view this thing? then do this action?`. 

## OAuth Users
Adding OAuth users with Microsoft, Google or Mastodon accounts is as simple as using their email or instance id. 
For Mastodon, you don't need the leading `@` symbol - e.g. `tezoatlipoca@mas.to` not `@tezoatlipoca@mas.to`

Basically, the OAuth user selects the OAuth method of choice by selecting Microsoft, Google or Mastodon login buttons on the Login page; the only thing GeFeSLE does is redirect to the relevent OAuth service where the user authenticates. When the OAuth service redirects back to GeFeSLE it will provide the _authenticated_ user credentials; all GeFeSLE does is check to see if those credentials are in its user database. 

_Nerd talk: The returning Claims Principal:Email is used and compared against username/email fields in the database._

## User data retention
GeFeSLE stores no user data. We honestly don't care to. You can't have exploited or leaked data you dont have. 
The only even-remotely sensitive information that the user database stores is a username and/or an email or fediverse ID. 
If the user account is local, there's a salted password too, but even if - somehow - the database were compromised it would be useless to anyone in its salted form. _Someone may want to fact check me on this_

GeFeSLE's database also does not store any session information. User sessions are server side only and once they time out (by default: 30m) the user will have to log in again. 
