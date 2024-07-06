# Installing GeFeSLE

GeFeSLE is a single binary webservice that relies on the Microsoft .NET 8.0 Core runtime. 

## Get .NET runtimes
_You'll have to lookup for your platform how to do this_
You can check to see if you have the Microsoft .NET platform installed by typing (in whatever shell you prefer)
`dotnet --info`

## Get the application
[Get the latest binaries for your platform - from the releases page](https://github.com/tezoatlipoca/GeFeSLE-server/releases)


## Installing on Windows
(haven't learned how to programmatically register a new Windows Service yet)
(also there isn't an install wizard yet)

1. extract the binaries somewhere
2. create a shortcut with this as its command line `<path to>\GeFeSLE.exe --config=./config.json`, customizing the path and name of your config file accordingly.
3. follow the customization instructions from [/Documentation/Customization.md]
4. run using your shortcut

## Installing on Linux (oversimpliflying somewhat)
(sorry no apt-get or zypper packages yet)
1. extract the binaries somewhere
2. create a bash shell to start it, like:
```
#!/bin/bash
echo "running.."
cd /home/tezoatlipoca/bin/gefesle
pwd
./GeFeSLE --config=./config.json > output.log 2>&1
```
.. customizing the path and name of your config file. 
3. if your distro supports, create a `systemd` service? 
```
tezoatlipoca@/etc/systemd/system> cat gefesle.service
[Unit]
Description=GeFeSLE
After=network.target

[Service]
Type=simple
#User=tezoatlipoca
Restart=always
WorkingDirectory=/home/tezoatlipoca/bin
ExecStart=/home/tezoatlipoca/bin/rungefsvc
#StandardOutput=file:/var/log/gefesle.out.log
#StandardError=file:/var/log/gefesle.err.log

[Install]
WantedBy=multi-user.target
```
4. follow the customization instructions from [/Documentation/Customization.md]
5. `systemctl` to start/stop/restart: `sudo systemctl start gefesle`


# Moving GeFeSLE (experimental)
In theory, since all of the HTML files for lists, as well as those for the web interface are either generated or packaged/rebuilt by the service when it restarts (or on demand), the only thing you need to take with you or do should you move GeFeSLE are:
* reinstall the software/move the binaries
* move the database file and your config file; move customization site/page header and footer files if applicable
* move the `wwwroot\uploads` folder. 
.. then modify your config file and restart!
Remember, everything in the `wwwroot` folder with the exception of the uploads folder will be eradicated and rebuilt from database/factory settings when you start GeFeSLE.
