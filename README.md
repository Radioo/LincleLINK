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

# How does it save space?

When adding subsequent instances, only unique files will be added to the `db`. For example say you have IIDX28 added as an instance, then you add your IIDX27 data as well. Only files unique to IIDX27 will be copied/moved, in turn `db` size will increase by about 4GB instead of 60GB.

![screenshot](https://stn.s-ul.eu/O84VELWa.png)
