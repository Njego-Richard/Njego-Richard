import React from 'react';
import  { useState }from 'react'
import axios from 'axios'



const Form = () => {
  const [formData,updateFormData]=useState("")
  const handleChange=(e)=>{
    updateFormData({ 
      ...formData,
      [e.target.name]: e.target.value.trim()

    })
  }
  const handleSubmit=(e)=>{
    e.preventDefault()
    console.log(formData)
  }
  const predict=(event)=>{

    event.preventDefault()

    axios({
      method: "post",
      url: "http://localhost:3002/python/postArg1",
      data: formData
    }).then((response)=>{
      console.log(response.data)
    })

    
  }



  return <>
      <form>
        <label>
          day
          <input name="day" onChange={handleChange}/>
        </label>
        <br/>
        <label>
          hospital
          <input name="hospital" onChange={handleChange}/>
        </label>
        <br/>
        <button onClick={predict}>Submit</button>
      </form>
  </>;
};

export default Form;
