# Instance paths match case-insensitively on every platform

An Update has to decide whether a dropped `Data/Graphic/x.ifs` is the same file as the Instance's `data/graphic/x.ifs`. The games this app manages are Windows games, where those are one file, and an Instance that holds both cannot be deployed correctly on Windows. We decided that paths inside an Instance match case-insensitively on Windows, Linux and macOS alike. The Instance's existing casing wins for directories and replaced files, and new paths keep the casing they arrive with. Add, Update and Duplicate all follow this, so an Instance never holds two paths that differ only by case.

## Considered options

- Match by the host file system's rules. Rejected because the same Instance would then behave differently per machine, and a database moved from Linux to Windows could hold entries that collide on deploy.
- Match exactly everywhere. Rejected because update packs often differ in case from the install they patch, and every such file would show up as a second copy instead of a replacement.

## Consequences

On Linux and macOS a source folder that really does contain `a.txt` and `A.txt` cannot be represented. The later file wins and the preview marks the conflict.
