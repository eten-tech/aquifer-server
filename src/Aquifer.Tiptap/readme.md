# Aquifer.Tiptap

We use Tiptap as a headless editor and data model for custom information we embed and display in resource contents.

Unfortunately, Tiptap does not have a .net implementation; it is JavaScript only.
We implemented our own custom rules in JavaScript in the [aquifer-tiptap](https://github.com/eten-tech/aquifer-tiptap)
repository which are used client side.

This project exists to import that repository (as a node "package" straight from GitHub) and compile the TypeScript
into JavaScript that can be used by the [V8 Engine](https://github.com/Microsoft/ClearScript) in .net.
This allows converting Tiptap JSON to HTML and back as needed for resource translation and other tasks.

In order to update the version of aquifer-tiptap used, edit the `package.json` file and reference the appropriate tag.
Example:

```
    "dependencies": {
        "aquifer-tiptap": "https://github.com/eten-tech/aquifer-tiptap#1.0.2"
    },
```

This project is currently configured to build with .net. Upon build, the TypeScript compiler will create a
`TiptapUtilities.js` file from the `aquifer-tiptap` "node module" in the `dist` directory.

If the `dist/TiptapUtilities.js` file exists on your local workstation then you can unload this project if desired
to stop it building (but note that you'll need to reload it to get any updates in the future).