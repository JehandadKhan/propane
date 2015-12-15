// Signature file for parser generated by fsyacc
module Parser
type token = 
  | UNORDERED
  | ORDERED
  | PATHS
  | SCOPE
  | DEFINE
  | FALSE
  | TRUE
  | OR
  | AND
  | SHR
  | STAR
  | NOT
  | UNION
  | INTERSECT
  | EOF
  | DOT
  | ROCKET
  | RARROW
  | SEMICOLON
  | COLON
  | SLASH
  | COMMA
  | RBRACE
  | LBRACE
  | RBRACKET
  | LBRACKET
  | RPAREN
  | LPAREN
  | EQUAL
  | INT of (System.Int32)
  | ID of (string)
type tokenId = 
    | TOKEN_UNORDERED
    | TOKEN_ORDERED
    | TOKEN_PATHS
    | TOKEN_SCOPE
    | TOKEN_DEFINE
    | TOKEN_FALSE
    | TOKEN_TRUE
    | TOKEN_OR
    | TOKEN_AND
    | TOKEN_SHR
    | TOKEN_STAR
    | TOKEN_NOT
    | TOKEN_UNION
    | TOKEN_INTERSECT
    | TOKEN_EOF
    | TOKEN_DOT
    | TOKEN_ROCKET
    | TOKEN_RARROW
    | TOKEN_SEMICOLON
    | TOKEN_COLON
    | TOKEN_SLASH
    | TOKEN_COMMA
    | TOKEN_RBRACE
    | TOKEN_LBRACE
    | TOKEN_RBRACKET
    | TOKEN_LBRACKET
    | TOKEN_RPAREN
    | TOKEN_LPAREN
    | TOKEN_EQUAL
    | TOKEN_INT
    | TOKEN_ID
    | TOKEN_end_of_input
    | TOKEN_error
type nonTerminalId = 
    | NONTERM__startstart
    | NONTERM_start
    | NONTERM_scopes
    | NONTERM_scope
    | NONTERM_cconstrs
    | NONTERM_cconstr
    | NONTERM_exprs
    | NONTERM_expr
    | NONTERM_pconstrs
    | NONTERM_pconstr
    | NONTERM_regexes
    | NONTERM_regex
    | NONTERM_predicate
    | NONTERM_definitions
    | NONTERM_definition
/// This function maps tokens to integer indexes
val tagOfToken: token -> int

/// This function maps integer indexes to symbolic token ids
val tokenTagToTokenId: int -> tokenId

/// This function maps production indexes returned in syntax errors to strings representing the non terminal that would be produced by that production
val prodIdxToNonTerminal: int -> nonTerminalId

/// This function gets the name of a token as a string
val token_to_string: token -> string
val start : (Microsoft.FSharp.Text.Lexing.LexBuffer<'cty> -> token) -> Microsoft.FSharp.Text.Lexing.LexBuffer<'cty> -> (Ast.T) 
