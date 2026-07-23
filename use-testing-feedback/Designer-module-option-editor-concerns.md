# Stakeholder Review Document: Designer and Editor concerns 2026-07-23

## Overview

This document outlines the concerns and considerations regarding the Designer module option editor and its interaction with the output port. The goal is to ensure that the design and functionality meet stakeholder expectations and provide a seamless user experience.

### User Experience Concerns
1. **Intuitive Interface**: The option editor should be easy to navigate, with clear labels and tooltips to guide users through the configuration process.
2. **Consistency**: The design should maintain consistency with other modules in terms of layout, color scheme, and interaction patterns to avoid confusion.
3. **Modal editor option**: The option editor should be presented in a modal window to keep the user focused on the task at hand without navigating away from the main interface.**
4. **Lack of Documentation and tooltips**: Users may struggle to understand the purpose and functionality of certain options without adequate documentation or tooltips. Providing clear explanations and examples can enhance usability.
5. **Ease of having options from variables**: Users should be able to easily configure options using variables, allowing for dynamic and flexible configurations. This can be achieved through a user-friendly interface that supports variable selection and input.
6. **General Navigation**: There is not currently a way to navigate back to the workflow designer from the main navigation bar. Users may find it inconvenient to return to the workflow designer after configuring options in the editor. Implementing a clear navigation path or a dedicated button can improve the overall user experience.
7. **Output Port Interaction**: The interaction between the option editor and the output port should be seamless. Users should be able to easily link options to the output port without confusion or additional steps. Clear visual cues and feedback can help users understand the relationship between options and output.
8. **Arrow dragging**: Dragging an arrow to lock to another node causes the arrow to render in the top left corner instead of pointing at the locked target. the actual edit functionality still works but the display behavior is confusing and may lead users to believe that the arrow is not properly connected. This issue should be addressed to ensure that the visual representation accurately reflects the underlying functionality.
