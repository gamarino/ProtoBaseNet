# Gemini

This file provides instructions and context for the Gemini AI model.
ProtoBaseNet is an implementation of an object inmutable database.
Collections are handled using AVL Tree as foundational structure. It should not be changed
Comments always written in professional english
At writing to storage, only attributes with field names not starting with '_' will be permanently written (public attributes). On load only public attributes are restored. It's responsability of a concrete class to recreate internal attributes if needed on _load. Be carefull with attribute naming


