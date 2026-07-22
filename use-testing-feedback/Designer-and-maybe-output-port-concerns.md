# Stakeholder Review Document: Designer and Maybe Output Port Concerns

## Overview
This document outlines the concerns and considerations related to the designer and the maybe output port in our system.
It aims to provide clarity on the design decisions and potential issues that may arise during implementation.

## Designer Concerns

### User Experience 

It is confusing that there is no way for me to just have a Fan-in with a single output; Maybe we are lacking documentation but I would like something like fan-in but where I can take the input properties and have them all just map to an output object.

#### Output Port Mapping

The way to properly map output ports to a single output object is not clear. It would be beneficial to have a mechanism that allows for easy aggregation of multiple output ports into a single JSON object.
As an example, if I have a component with non-error output ports 'foo, bar, baz' I want it to be easy for me to get a single json output where those ports are properties of the resulting object.

##### Developer notes:

- We need to make sure that this can be done in a performant way, I'm not sure if we need to update the module or executor interface/base accordingly to support or not?

#### Loop Exposure in UX

I don't understand how to build a loop in the UI. I think it would be nice to expose the loop construct in the designer so that users can easily create loops; almost like a 'subwindow' to aid in visualizing the loop structure. This would help users understand how to implement loops without needing to dive into complex configurations.

#### Try-Catch Exposure in UX

Similar to above, it would be beneficial to expose try-catch constructs in the designer. This would allow users to visually manage error handling and understand how to implement error recovery strategies without needing to write complex code.

#### Editor panel

It does not look like the drop downs populate, for instance on the 'builtin.transform.join' component, the 'join type' drop down does not populate with options. This makes it difficult for users to understand what options are available and how to configure the component correctly.

#### Documentation and Guidance

The lack of documents built in (i.e. not linking to github pages) is a problem. Users need clear, accessible documentation within the designer to understand how to use components effectively. This includes examples, best practices, and troubleshooting tips.

