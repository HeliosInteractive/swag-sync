# swag-sync

S3 file sync utility (USEast1 only)

## building

### Windows
Open up Visual Studio and do a "Rebuild All".

##Linux (Ubuntu)
Follow instructions on [mono-project](http://www.mono-project.com/docs/getting-started/install/linux/)'s website to add mono-project to your `apt-get` repos, Specifically:

```BASH
sudo apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF
echo "deb http://download.mono-project.com/repo/debian wheezy main" | sudo tee /etc/apt/sources.list.d/mono-xamarin.list
sudo apt-get update
```

Then install mono-complete, sqlite, and nuget:
```BASH
apt-get install mono-complete nuget libsqlite3-0
```

Restore nuget packages and build:
```BASH
cd <where swag-sync.sln is>
nuget restore swag-sync.sln
xbuild swag-sync.sln
```
