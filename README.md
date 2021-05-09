Neo Geo X Game Card Utility
===========================

This program encrypts/decrypts and generates the necessary files for creating a
game card.

Preparation
-----------
Your ROMs need to be processed with `fbacache`. Games are named according to
`gameX.aes` or `gameX.mvs`, where `X` is the game index starting from 1. Each
game also needs a corresponding `gameX.png` thumbnail, which is a 209x143
image. Place all of these flat in a folder.

Usage
-----

### Creating a game card
```
dotnet NgxDecrypt.dll pack <inPath> <outPath>
```
- `inPath`: the folder of games that you've prepared.
- `outPath`: output folder path. This should be a `card_game` folder at the
  root of your SD card.

### Decrypting a game card
```
dotnet NgxDecrypt.dll unpack <inPath> <outPath>
```
- `inPath`: path to the `card_game` folder on your game card.
- `outPath`: path where you want to decrypt the ROMs to.

In any case, `outPath` folder is created if it does not exist, and files
inside will be overwritten if already existing.

You can use this tool to convert between the old and new game card formats for
before and after the v500 update.
