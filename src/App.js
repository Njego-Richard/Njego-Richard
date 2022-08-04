//import logo from './logo.svg';
import './App.css';
import axios from 'axios'
import React , { useState, useEffect }from 'react'
import Form from "./components/Form"



function App() {
 const [x,setX]=useState("")

 useEffect(()=>{

    /*const config={
      method: "get",
      url: "http://localhost:3002/users/a"
    }
    let Request=axios(config)
    Request.then((response) => {

      setX(response.data)
    },[])*/

    // this is the javascript http client method
   axios.get('http://localhost:3002/users/a').then((response) => {

      setX(response.data)
      //const a=response.data;
      //console.log(a)
  
  
  })

  /*var body={ 
    a: "njego",
    b:"richie"
  }
  axios({
    method: "post",
    url: "http://localhost:3002/python/postArg1",
    data: body
  }).then((response)=>{
    console.log(response) 
  })*/


  },[])
  return (
    <div className="App">
      
        <p>
          {x}
          save to reload.
        </p>

    <Form/>
     
    </div>
  );
}

export default App;
