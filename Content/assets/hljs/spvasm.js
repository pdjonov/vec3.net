function hljsDefineSPVASM(hljs) {
    return {
        name: 'SPIR-V',
        contains: [
            hljs.COMMENT(/;/, /$/),
            hljs.QUOTE_STRING_MODE,
            hljs.C_NUMBER_MODE,
            {
                className: 'variable',
                begin: /%\w+/
            },
            {
                className: 'built_in',
                begin: /Op[A-Z][a-zA-Z]*/
            },
            {
                className: 'operator',
                begin: /=/
            }
        ]
    };
}