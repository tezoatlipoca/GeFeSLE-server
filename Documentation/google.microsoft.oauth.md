# Google, Microsoft - OAuth Setup
Here we cover how to register your GeFeSLE instance with Google or Microsoft to allow OAuth logins by other users and to import notes and tasks from those services. 
[#Microsoft OAuth]
[#Google OAuth]
[#Mastodon OAuth]

## Microsoft OAuth
In order to allow OAuth login from Microsoft account holders YOU need to have a microsoft account; this gets you access to the Microsoft Azure developer portal where we will register your GeFeSLE instance as an app.
Before we begin, make sure you have your Microsoft account ready. 
### 1. Log into your Microsoft Azure developer
(https://portal.azure.com/#home) Don't worry about subscriptions or plans or anything we don't need em. 
![microsoft app portal](microsoft1.PNG)

### 2. Search for "App Registrations"
![In the search bar at the top look for App Registrations - you can see I've been there before so its showing it under "recent"](microsoft2.PNG). Click on that. 

### 3. Click "+ New Registration"
![Click where it says + New Registration](microsoft3.PNG)

### 4. Register an application
![Fill out app name, what type of MS accounts you want, and the Redirect URI - NOT OPTIONAL unlike what it says there.](microsoft4.PNG)

**Name** - this is the user facing app name, in other words what will be asking the user's permission to log them in (and get access to their data to import). You can change this later, but recommend something like "3Kitten's Big List of Lists" or your actual domain, what you set for `ServerSettings:Hostname` in your configuration file. 
**Supported account types** - sets what types of accounts you want to allow access. The middle one handles both personal/individual MS accounts and those for users at schools/corporations who's Microsoft accounts are _administered_. 
**Redirect URI** - this is the URI to which Microsoft reaches out after the user has logged in, done 2FA and otherwise proven (to Microsoft) they are who they say they are, where MS says to your instance "ok, they're legit, back to you". In our case what you specify here has to be the public facing hostname of your site (`ServerSettings:Hostname` from your configuration file) plus the _required_ `/signin-microsoft` endpoint provided by the ASP.NET Microsoft middleware. This _includes_ the protocol (HTTP, HTTPs://) In my case, my instance Hostname is `https://lists.awadwatt.com` so my Redirect URI is `https://lists.awadwatt.com/signin-microsoft`.

Once done, click **Register**
This registers your app and creates a few magic keys that we need to specify in our config file:
![](microsoft5.PNG). 

**Application (client) ID** this is `microsoftclientId` in your config file (example below)
**Directory (tenant) ID** this is `microsoftTenantId` in your config file (example below)

```
"OtherSites": {
        "Microsoft": {
            "microsoftClientId" : "********-****-****-****-************",
            "microsoftClientSecret" : "************",
            "microsoftTenantId" : "********-****-****-****-************"
        }
},
```


### 4. Create a Secret
On the page where you copied the client and tenant ids, find the **Client credentials** on the right and click **Add a certificate or secret**. 

On the next page that appears, showing app certificates and client secrets, click **+ New client secret**

![click + new client secret](microsoft6.PNG)

It will prompt you for a "name" of this secret and an expirty duration (after which you'll have to come back here and generate a new one). Do so and click the appropriate button. 

Back in the certificates and Client secrets page your newly generated Client Secret value and ID is shown. Copy the **Value**

![copy the value, not the ID](microsoft7.PNG)

this becomes the `microsoftClientSecret` in your config file (example above). _If you accidentally copy your secret ID instead of the _value_, GeFeSLE will cough up an exception that says so when you try and log in via OAuth. 

### Save, restart and try it
Save the 3 parameters we copied from your app registration in the Microsoft Azure developer portal in your GeFeSLE configuration file and restart GeFeSLE. 

**Make sure** you have already added your Microsoft account as a "user" in the user management panel; it shouldn't matter whether you put your Microsoft account email as the username or the actual email, it _should_ check both. 

Now logout/kill any session you may currently have; go to the Login page; select Microsoft.
![login with Microsoft from the login page](microsoft9.PNG) It will redirect you to Microsoft where you specify the Microsoft account you wish to use:

![enter the account you want to use; it should be the one you added as a GeFeSLE user before](microsoft10.PNG) and click Next

_At this point you may have to do additional account selection or maybe some MFA/2FactorAuthentication_

Eventually you'll be presented with a request by your instance to gain access to your account: 
![approve it; its the app you just setup](microsoft8.PNG). Click Accept. 

If all goes well, you've logged in:
![come on in](microsoft11.PNG)

## Wait, why is my (or _A_) GeFeSLE instance asking for access to my email? 
Your instance - via its app registration - is asking for:
* access to your _profile_ so it can perform OAuth log ins. This is required. 
* access to your _email_ because Microsoft Sticky Notes are actually stored in a special folder in your Microsoft/Outlook.com email store. (_yeah, the Sticky Notes you use in Windows are synced to your Microsoft Account via your email)

TODO: permit two app registrations for the Microsoft service, one that is used JUST for OAuth logins and the other for Sticky Note import. 

## So what information from my Microsoft Account does a GeFeSLE instance actually read or store? 
Outside of importing Sticky Notes, absolutely nothing. When the OAuth control is transfered back to GeFeSLE from Microsoft's authentication backend, we check to see that the claims (i.e. your username or email) are in our user database, and if they are we store the token we receive from Microsoft to access various APIs on your behalf. Right now the only APIs we use this for are the mail APIs to get Sticky Notes. AND, the API token (from Microsoft) we cache only lives as long as your GeFeSLE instance browser session, which right now times out after 30 minutes anyway; none of this is stored in the instance's database or persisted in any way. 

## Google OAuth
In order to allow OAuth login from Google account holders YOU need to have a Google account; this gets you access to the Google Cloud Console developer portal where we will register your GeFeSLE instance as an app.
Before we begin, make sure you have your Google account ready. 

### 1. Log into your Google Cloud developer console
(https://console.cloud.google.com) Don't worry about subscriptions or plans or anything we don't need em. 
![google cloud console](google1.PNG)

### 2. create a new project
From the `Select a project` dropdown at the top; then go `New Project`
![select project > new project](google2.PNG)

### 3. name the project
This is just for you, its not the name your users will see. 
![pick a good project name;something inspiring](google3.PNG)

### 4. enable some APIs
Here's where we tell the Google Cloud what elements of their user's data we want access to when they authenticate. 
**_If you don't want your users to import from Google Tasks you can skip the APIs_**
Make sure your new project is selected in the dropdown at the top
![like this](google4.PNG)
then from the menu at the left select 
`APIs & Services` then `Enabled APIs & Services`
![](google5.PNG)
and then finally 
![Enable APIS AND SERVICES](google6.PNG)
this brings up the API Library.
![behold: the library where thy APIs are kept](google7.PNG)
Search for the `Google Tasks API`
![NOT the Cloud Tasks API](google8.PNG)
Add it/enable it. Should look like this now:
![google Tasks API enabled](google9.PNG)

### 5. add some Credentials
Here's where we specify details that uniquely identify US (as in YOUR GeFeSLE instance).
Under `APIS & Services` now find `Credentials`
![APIs & Services > Credentials](google10.PNG)
.. then `Create Credentials`
![Create Credentials](google11.PNG)
.. then `Create OAuth client ID`
![Create OAuth client ID](google12.PNG)
Click `Configure Consent Screen`; select `External`
![External](google13.PNG)
when prompted, fill in your app name and YOUR contact/support email. 
THIS is the app or sitename that your users will see when they get prompted for "_Steve's Website of Useful Lists_ wants to access your data". 
![now if Steve had a list of Taco Trucks](google14A.PNG)
Fill in the relevant web pages - you can just link to your GeFeSLE's index if you want. 
![but good for you if you have a privacy policy and terms of service](google14B.PNG)
then add authorized domains and your contact info. 
![don't use mine; I don't want msgs about your Big List of Taco Trucks](google14C.PNG)

### 6. Scopes
Heres where we tell Google how we're gonna use the APIs from before to access the user's data (or not). 
The permission scopes we need will depend on what APIs we selected above. 
![non-sensitive scopes are just the user's email address](google15.PNG)
All we need is the user's primary google Account email address and the personal info they've chosen to make publically available. 
![although really all we're looking for is their email address](google15A.PNG)
If you want your users to import Google Tasks, you have to check that API (we only need `.readonly`):
![only need ../auth/tasks.readonly if users will be importing their Google Tasks](google15B.PNG)
Your sensitive and non-sensitive scopes should look like this:
![my sister karved her scopes on a moose once](google15C.PNG)


UNDER CONSTRUCTION
