# LincleLINK

This program is designed to store all your arcade data in its own `db` folder. When you add an instance, it produces an MD5 hash of the file and stores it in the `db` folder. Only new and unique files are added, but the record of the file and its location in the original folder is also stored in the instance `.json` file.

You can then create hard links to your desired destination(s).

***It is highly recommended to only add the `data` folder of a game as an instance.***
***Also remember that hard links only work on the same partition, additionally editing/modifying hard-linked files will change the originals in the `db` so take extra care. Delete a hard-linked file first if you want to replace/modify it!***

![screenshot](https://stn.s-ul.eu/O84VELWa.png)
