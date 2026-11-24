# Airports

[User guide](README.md) › Airports

Photo scenery packages bring their own, more detailed ground. Some airports then sit on a bump or in a hollow of it: the runway slopes, the terminal stands on a hill, parked aircraft lean. The **Airports** page in the sidebar fixes that one airport at a time: **Flatten** asks X-Plane to level the ground under the airport.

## Finding an airport

- Type in the search box: an **ICAO code** (or its start), or part of the airport's **name or city**. An exact code comes first, then codes that start with it, then names and cities. The page shows the first 50 matches; type more to narrow them.
- With the search box empty, the page lists **the airports you flattened**.
- Under each airport's name is its place and where X-Plane takes the airport from, for example *from Global Airports* (X-Plane's own airports) or the name of a custom scenery package.
- The page needs the **X-Plane folder** to be set in [Settings › Folders](settings.md#folders); until then it says *Choose your X-Plane folder first*. It reads the airports of the scenery packages switched on in X-Plane's scenery list each time you open it. **Refresh** reads them again, for example after you installed an airport while the page was open.

## Flattening and undoing

- **Flatten** levels the ground under the airport. The row then says **Flattened by OrthoStreamXP**.
- **Undo** takes the flattening out: X-Plane uses the airport as its scenery has it again.
- One airport is changed at a time. While it is being changed, the row says *Working…*.
- **Already flattened by its own scenery** means the airport's scenery levels the ground already: there is nothing to do, and the row has no button.

> **Custom airports:** use **Flatten** for X-Plane's own airports, not for custom airport scenery. A custom airport loses its buildings and other scenery when the flattened copy is above it. The page reminds you of this at the top: in X-Plane's scenery list, keep **OrthoStreamXP Flattened Airports** below your custom airports (see [X-Plane Custom Scenery](custom-scenery.md)).

## When changes apply

- X-Plane reads the airports when it starts, so a change shows from **X-Plane's next start**.
- You can flatten or undo while OrthoStreamXP is stopped or while it runs, but not while it is starting or stopping. OrthoStreamXP does not need to be restarted.

## How it works

OrthoStreamXP never changes an airport's own files. It puts a copy of the airport, marked to be flattened, in a scenery package of its own: **OrthoStreamXP Flattened Airports** in X-Plane's `Custom Scenery` folder. It lists that package in X-Plane's scenery list just above the scenery the airport comes from, because X-Plane uses an airport from the highest package in the list that has it. **Open the folder** opens the package in Explorer.

**Undo** takes the airport's copy out of the package. Undoing the last one removes the package and its line in the scenery list.

## When a row shows a warning

| The row says | What to do |
|---|---|
| **Flattened, but its scenery changed since** | The airport's layout changed after you flattened it, for example after an X-Plane update, and the copy still has the old one. Click **Flatten again** to take the new layout. |
| **Flattened, but X-Plane uses** *(package)***'s copy** | **OrthoStreamXP Flattened Airports** is below that package in the scenery list, or switched off. Click **Flatten again** to switch it on and move it just above. |
| *Its scenery package is no longer in the scenery list* | The package the airport came from is gone or switched off, and only the flattened copy is left. Click **Undo** if you no longer want it. |

Next: [Troubleshooting](troubleshooting.md)
