namespace Seff.Util

open System

/// simple parsing of Fsharp code
module ParseFs = 
    
    type State =     
        |Code
        |InComment      
        |InBlockComment 
        |InString       
        |InAtString      // with @
        |InRawString     // with """
    
    [<Struct>]
    type Position = {offset:int; line:int}

    /// a find function that will automatically exlude string , character literals and  comments from search
    /// the search function shall return true on find sucess
    /// even if fromIdx is a high value the search always starts from zero to have correct state
    let findInFsCode search fromIdx (tx:string) = 
       
        let lasti = tx.Length-1
        if fromIdx > lasti then eprintfn "findInCode: Search from index %d  is bigger than search string last index %d" fromIdx lasti
        let mutable i = 0
        let mutable line = 1

        let inline isCh (c:Char) off i =
            i+off <= lasti && tx.[i+off] = c

        let checklast state =
           if i > lasti then ValueNone 
           else
               let t = tx.[i]
               match state with
               |Code ->  if i >= fromIdx && search(i) then ValueSome {offset=i; line=line} else ValueNone
               | InComment  | InBlockComment  | InString  | InAtString  | InRawString -> ValueNone
                

        let rec find advance state  = 
            i <- i + advance   
            if i >= lasti then checklast state 
            else
                let t = tx.[i]
                let u = tx.[i+1]
                match state with 
                |Code -> 
                    if i >= fromIdx && search(i) then ValueSome {offset=i; line=line}
                    else                     
                        match t,u with 
                        | '"' , '"'  when  isCh '"' 2 i  -> find 3 InRawString
                        | '/' , '/'   ->  find 2 InComment
                        | '(' , '*'   ->  find 2 InBlockComment
                        | '@' , '"'   ->  find 2 InAtString
                        | '"' ,  _    ->  find 1 InString    //advance just one next char might be escape char
                        // a char:
                        | ''' , '\\' -> // a escaped character
                            if   isCh 'u'  2 i  &&  isCh ''' 7 i  then find 8 state  // a 16 bit unicode character
                            elif isCh 'U'  2 i  &&  isCh ''' 11 i then find 12 state // a 32 bit unicode character
                            else find 4 state  // a simple escaped character
                        | ''' , _ ->                 find 3 state    // jump over a regular  character, including quote " and quote ' 
                        | '\n', _ ->   line<-line+1 ;find 1 state    
                        |  _      ->                 find 1 state                                

                | InComment -> 
                    if  t='\n' then line<-line+1 ; find 1 Code 
                    else find 1 state 
            
                | InBlockComment ->
                    if   t = '*' && u = ')'  then find 2 Code 
                    elif t =  '\n' then   line<-line+1 ;find 1 state
                    else  find 1 state 

            
                | InString ->
                    match t,u with 
                    | '\\' , '"'    -> find 2 state
                    | '"'  , _      -> find 1 Code
                    | '\n', _       -> line<-line+1 ;find 1 state  
                    | _             -> find 1 state                             
            
                | InAtString ->
                    match t with 
                    | '"'    -> find 1 Code 
                    | '\n'   -> line<-line+1 ;find 1 state  
                    | _      -> find 1 state       

                | InRawString ->
                    match t,u with 
                    | '"' , '"'  when  isCh '"' 2 i  -> find 3 Code
                    | '\n', _    ->   line<-line+1 ;find 1 state 
                    | _                          -> find 1 state
        
        find 0 Code     // 0 since inital 'i' value is 0
    
    /// a find function that will search full text from index
    /// the search function shall return true on find sucess
    let findInText search fromIdx (tx:string) =        

        let len = tx.Length
        if fromIdx > len-1 then eprintf "findInText:Search from index %d  is bigger than search string %d" fromIdx len
        
        let mutable line = 1                        

        let rec find i  = 
            if i = len then ValueNone 
            else                
                if search(i) then ValueSome {offset=i; line=line}
                else 
                    if tx.[i] = '\n' then line <-line + 1
                    find (i+1)         
        find fromIdx     

    /// Only starts search when not in comment or string literal
    /// Will search one char backward backwards from current position.
    /// Last charcter of search can be a quote or other non search delimter.
    /// Since it searches backward this allows to find ending blocks of strings and comments too,
    let findWordBackward (word:string) fromIdx (inText:string) =
        let last = word.Length-1       

        let search (i:int) =
            if i-1 < last then false // word longer than index value
            else                
                let mutable iw = last
                let mutable it = i-1            
                while iw >= 0 do                   
                    if inText.[it] = word.[iw] then 
                        iw <- iw-1
                        it <- it-1
                    else                             
                        iw <- Int32.MinValue //exit while
                iw = -1 

        match findInFsCode search fromIdx inText with 
        |ValueSome p -> 
            let off = p.offset - last - 1
            let ln = p.line - (Str.countChar '\n' word ) 
            Some {offset = off ; line = ln}
        |ValueNone   -> 
            // it still might be at the end of string so do this extra search:
            if inText.EndsWith word then 
                let off = inText.Length - word.Length
                let ln = 1 + (Str.countChar '\n' inText) - (Str.countChar  '\n' word) // line count starts at one
                Some {offset = off ; line = ln}
            else
                None

    /// Only starts search when not in comment or string literal
    /// Since it searches forward this allows to find starting blocks of strings and comments too
    let findWordAhead (word:string) fromIdx (inText:string) =
        let len = word.Length

        let search (i:int) =            
            if i+len >= inText.Length then false // search would go over the end of text
            else                
                let mutable iw = 0
                let mutable it = i            
                while iw < len do                    
                    if inText.[it] = word.[iw] then 
                        iw <- iw+1
                        it <- it+1
                    else                             
                        iw <- Int32.MaxValue //exit while
                iw = len            
        
        match findInFsCode search fromIdx inText with 
        |ValueSome p -> Some p
        |ValueNone   -> None    
    
