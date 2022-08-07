# LincleLINK

# How does it work?

## File storage
The tool's main purpose is to store all your data in its own `db` folder. The files are renamed to their MD5 hash which ensures only unique files are added to the `db` folder.

## Instances
Instances are simply a record of: file names, their hashes and their location whithin the original data structure. When adding an instance, you have an option of choosing to copy or move files to the `db`. Moving is advised to reduce space and disk stress. ***It is highly recommended to only add the `data` folder of a game as an instance.***  
Here's an example of one file record:  
```
    {
      "FileName": "25063_pre.2dx",
      "RelativePath": "sound\\25063",
      "FileSize": 463806,
      "HashedFileName": "7AFE6AC1B80128D44BA5357D4349B21A.2dx"
    },
```

## Linking
When you've added at least one instance, you can link it to your desired destination. The tool will make hard links and keep the original file names and directory structure of the original files. ***Hard links only work on the same partition. If you wish to edit/replace a hard-linked file, delete it first! Directly modifying hard-linked files will alter the originals in the `db`!***

## Link to torrent
This feature was designed to allow you to prepare files for downloading a new style/version of a game. It uses your selected instance as a "base" and checks files against the pieces found in the `.torrent` file (https://en.wikipedia.org/wiki/Torrent_file). When the check is finished, you can link matched files to your desired destination and point your torrent client to check and download missing files. Using this feature ensures only files present in pieces with exact 100% match will be linked, this means your client should not overwrite your original files.  

`Instance to link from` - The instance you want to use as a "base", use the one that will have the most common files with the torrent.  
`Torrent file path` - The torrent file you wish to download.  
`Torrent relative data path` - Location of the `data` folder within the torrent file structure. Usually it will be `contents\data\`. Using `Check files` will perform a quick file name and size comarsion and will let you know if you got the right path.  
`Torrent download and link target location` - Your desired download location. The tool will recreate the folder structure found in the `.torrent` file. This is the location where you want to point your torrent client to download.  

![Link to torrent usage](https://stn.s-ul.eu/mIFRwafZ.png)

# How does it save space?

When adding subsequent instances, only unique files will be added to the `db`. For example say you have IIDX28 added as an instance, then you add your IIDX27 data as well. Only files unique to IIDX27 will be copied/moved, in turn `db` size will increase by about 4GB instead of 60GB.

![screenshot](https://stn.s-ul.eu/O84VELWa.png)
