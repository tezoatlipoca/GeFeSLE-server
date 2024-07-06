# Installing GeFeSLE

GeFeSLE is a single binary webservice that relies on the Microsoft .NET 8.0 Core runtime. 

## Get it








# Moving GeFeSLE (experimental)
In theory, since all of the HTML files for lists, as well as those for the web interface are either generated or packaged/rebuilt by the service when it restarts (or on demand), the only thing you need to take with you or do should you move GeFeSLE are:
* reinstall the software/move the binaries
* move the database file and your config file; move customization site/page header and footer files if applicable
* move the `wwwroot\uploads` folder. 
.. then modify your config file and restart!
Remember, everything in the `wwwroot` folder with the exception of the uploads folder will be eradicated and rebuilt from database/factory settings when you start GeFeSLE.
