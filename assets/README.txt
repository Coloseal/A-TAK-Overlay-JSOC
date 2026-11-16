ABOUT THE PICTURES (assets\)

The program draws a picture of a rugged tablet (device.png), a helicopter multi-function display (mfd.png), and uses a badge
(emblem.png / icon.ico) for its icon.

The photos used in the OFFICIAL RELEASES are other people's pictures and are NOT part of this open-source repository
(they are not covered by the MIT license, and are not published here).

You can still build the project: if a picture is missing, build.ps1 runs tools\make-placeholder-art.ps1, which draws simple
stand-ins. To use your own pictures, put them in this folder with these names and shapes, then run build.ps1:

  device.png   tablet photo, about 774 x 396 shape (any size with the same shape; the screen sits at logical x126 y69 w522 h250).
               White backdrop is cut away automatically.
  mfd.png      display picture, 780 x 569 shape, transparent outside the device (screen at logical x100 y95 w582 h387).
  emblem.png   256 x 256 badge with transparent corners.
  icon.ico     the program icon (several sizes).

The measurements are in src\Themes.cs (class Layouts).

assets\app.manifest is the program's Windows manifest (asks for no administrator rights).
